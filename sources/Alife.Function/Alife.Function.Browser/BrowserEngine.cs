using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class NavigateResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
}

public class BrowserEngine : IDisposable
{
    public async Task WaitToLoadedAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!worker.IsLoaded)
        {
            await Task.Delay(100, cts.Token);
        }
    }

    /// <summary>
    /// 跳转到指定页面
    /// </summary>
    public Task<NavigateResult> NavigateAsync(string url)
    {
        return worker.AddFormTask(async webView => {
            var tcs = new TaskCompletionSource<NavigateResult>();
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate(url);
            return await tcs.Task;

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                tcs.SetResult(new NavigateResult { Success = e.IsSuccess, StatusCode = (int)e.WebErrorStatus });
            }
        });
    }

    /// <summary>
    /// 执行JavaScript并易读的结果
    /// </summary>
    public Task<string> ExecuteScriptAsync(string code)
    {
        return worker.AddFormTask(async webView => {
            string wrapperScript =
                $$$"""
                   (function() {
                       const logs = [];
                       const originalLog = console.log;
                       
                       console.log = (...args) => {
                           logs.push(args.map(v => typeof v === 'object' ? JSON.stringify(v) : String(v)).join(' '));
                       };

                       try {
                           const rawCode = {{{JsonSerializer.Serialize(code)}}};
                           let result;

                           try {
                               result = eval(rawCode);
                           } catch (e) {
                               if (e instanceof SyntaxError) {
                                   result = eval("(function() {\n" + rawCode + "\n})()");
                               } else {
                                   // 如果是运行时错误（代码执行到一半报错），直接抛出，拒绝重试
                                   throw e;
                               }
                           }
                           
                           const finalValue = result;
                           
                           let output = "";
                           
                           if (typeof finalValue !== 'undefined' && finalValue !== null) {
                               output += typeof finalValue === 'object' ? JSON.stringify(finalValue, null, 2) : String(finalValue);
                           } else {
                               output += "[执行成功，无返回值]";
                           }
                           
                           //追加控制台日志（如果有）
                           if (logs.length > 0) {
                               output += "\n\n[Console Logs]\n" + logs.join('\n');
                           }
                           
                           return output.trim();
                           
                       } finally {
                           console.log = originalLog;
                       }
                   })();
                   """;
            var result = await webView.CoreWebView2.ExecuteScriptWithResultAsync(wrapperScript);
            if (result.Succeeded)
            {
                result.TryGetResultAsString(out string stringResult, out int isSuccess);
                stringResult = isSuccess == 1 ? stringResult : result.ResultAsJson;
                return $"[Success] Return:\n{stringResult}";
            }

            var ex = result.Exception;
            return $"[Error]\nName: {ex.Name}\nMessage: {ex.Message}\nDetail: {ex.ToJson}\nLocation: Line {ex.LineNumber}, Column {ex.ColumnNumber}";
        });
    }

    /// <summary>
    /// 观察当前页面，返回格式化后的页面信息，同时会对可交互组件增加data-alife-id属性
    /// </summary>
    public async Task<string> ObserveAsync(int page)
    {
        //等待页面稳定
        while (worker.IsNavigating)
        {
            await Task.Delay(300);
        }

        int currentPage = page < 1 ? 1 : page;
        string jsCode = $$$"""
                           (() => {
                               const M_CLS = 'al-m';
                               const TEXT_LIMIT = 1000;  // 期望单页文本上限
                               const ITEM_LIMIT = 20;   // 期望单页按钮上限
                               const ATTR_OLD = 'data-al-old';
                               const scope = {{{currentPage}}};
                               
                               document.querySelectorAll('.' + M_CLS).forEach(e => e.remove());
                               document.querySelectorAll(`[${ATTR_OLD}]`).forEach(e => {
                                   e.style.display = e.getAttribute(ATTR_OLD);
                                   e.removeAttribute(ATTR_OLD);
                               });
                               document.querySelectorAll('[data-alife-id]').forEach(e => e.removeAttribute('data-alife-id'));

                               let id = 0;
                               const map = {};
                               const getT = e => (e.innerText || e.value || e.placeholder || e.title || e.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 50);

                               const targetNodes = [];
                               for (const n of document.querySelectorAll('body *')) {
                                   if (!n.offsetWidth || ['SCRIPT', 'STYLE', 'SVG', 'META'].includes(n.tagName)) continue;
                                   const s = window.getComputedStyle(n);
                                   const isI = s.cursor === 'text' || ['INPUT', 'TEXTAREA'].includes(n.tagName) || n.isContentEditable;
                                   const isB = s.cursor === 'pointer' || ['A', 'BUTTON'].includes(n.tagName) || n.getAttribute('role') === 'button';
                                   if (!isI && (n.closest('a') && n.tagName !== 'A' || n.closest('button') && n.tagName !== 'BUTTON')) continue;

                                   if (isI || isB) {
                                       const t = getT(n), h = n.href || '';
                                       if (!isI && !t && !h) continue;
                                       const cur = ++id;
                                       n.setAttribute('data-alife-id', cur);
                                       map[cur] = { t: t || (isI ? '输入框' : '按钮'), h: h ? h.substring(0, 150) : '', isI };
                                       n.setAttribute(ATTR_OLD, n.style.display);
                                       n.style.display = 'none';
                                       const m = document.createElement('span');
                                       m.className = M_CLS;
                                       m.innerText = `[${cur}]`;
                                       m.style.cssText = 'position:absolute;opacity:0;font-size:1px;pointer-events:none;';
                                       n.after(m);
                                       targetNodes.push(n);
                                   }
                               }

                               const fullText = (document.body?.innerText || '').replace(/\s+/g, ' ').trim();

                               document.querySelectorAll('.' + M_CLS).forEach(e => e.remove());
                               for (const n of targetNodes) {
                                   n.style.display = n.getAttribute(ATTR_OLD);
                                   n.removeAttribute(ATTR_OLD);
                               }

                               // --- 核心：复合分页逻辑 ---
                               const totalItems = id;
                               // 总页数取文本页数和按钮页数的最大值
                               const totalPages = Math.max(
                                   Math.ceil(fullText.length / TEXT_LIMIT), 
                                   Math.ceil(totalItems / ITEM_LIMIT), 
                                   1
                               );
                               // 根据总页数计算本页应截取的平均字符步长
                               const stride = Math.ceil(fullText.length / totalPages);
                               const startIdx = (scope - 1) * stride;
                               let endIdx = startIdx + stride;
                               
                               if (endIdx < fullText.length) {
                                   const nextSpace = fullText.indexOf(' ', endIdx);
                                   if (nextSpace !== -1 && (nextSpace - endIdx) < 30) endIdx = nextSpace;
                               }
                               const pageContent = fullText.substring(startIdx, endIdx);
                               // ---------------------------

                               const found = [];
                               const re = /\[(\d+)\]/g;
                               let m;
                               while ((m = re.exec(pageContent)) !== null) {
                                   if (!found.includes(m[1])) found.push(m[1]);
                               }

                               const ins = [], btns = [];
                               for (const k of found) {
                                   const i = map[k];
                                   if (i.isI) ins.push(`${i.t}[${k}]`);
                                   else btns.push(`${i.t}[${k}]${i.h}`);
                               }

                               let out = `标题:${document.title}\n链接:${location.href}\n分页:${scope}/${totalPages}`;
                               if (scope < totalPages) out += ` (注意！当前页面显示不完整，请使用 page=${scope + 1} 来查看下一页)`;
                               out += `\n\n${pageContent}\n\n`;

                               if (ins.length) out += `--INPUTS--\n${ins.join('\n')}\n\n`;
                               if (btns.length) out += `--BUTTONS--\n${btns.join('\n')}`;
                               
                               return out.trim();
                           })();
                           """;


        return await ExecuteScriptAsync(jsCode);
    }

    readonly WebViewWorker worker = new();

    public void Dispose() => worker.Dispose();
}
