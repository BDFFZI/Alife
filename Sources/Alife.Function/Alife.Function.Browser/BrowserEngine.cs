using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class NavigateResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
}

public class BrowserEngine : IAsyncDisposable
{
    public async Task WaitToLoadedAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!worker.IsLaunched)
        {
            await Task.Delay(100, cts.Token);
        }
    }

    public async Task<NavigateResult> OpenWebsiteAsync(string url)
    {
        await worker.ClearPopupWindows();//跳转操作总是使用主窗口
        return await worker.ExecuteTaskAsync(async webView => {
            var tcs = new TaskCompletionSource<NavigateResult>();
            webView.CoreWebView2.NavigationCompleted += OnCompleted;
            webView.CoreWebView2.Navigate(url);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != tcs.Task)
                webView.CoreWebView2.NavigationCompleted -= OnCompleted;
            return completed == tcs.Task ? await tcs.Task : new NavigateResult { Success = true, StatusCode = 0 };

            void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= OnCompleted;
                tcs.TrySetResult(new NavigateResult { Success = e.IsSuccess, StatusCode = (int)e.WebErrorStatus });
            }
        });
    }
    public async Task<string> ReadWebsiteAsync(int page, int maxLength = 700)
    {
        //等待页面稳定
        while (worker.IsNavigating)
            await Task.Delay(300);

        string jsCode = $$"""
                          (function () {
                              function canVisible(el) {
                                  if (!el) return false;
                                  if (el.nodeType !== Node.ELEMENT_NODE) return true;
                                  const style = getComputedStyle(el);
                                  return style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0' && !el.classList.contains('sr-only');
                              }

                              function canClick(el) {
                                  if (el === document.body) return false;
                                  if (el.tagName === 'A' || el.tagName === 'BUTTON' || el.onclick) return true;
                                  if (el.nodeType === Node.ELEMENT_NODE) {
                                      const role = el.getAttribute('role');
                                      if (role === 'link' || role === 'button') return true;
                                      if (el.hasAttribute('tabindex')) return true;
                                  }
                              }

                              function canEdit(el) {
                                  if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) return true;
                                  if (el.nodeType === Node.ELEMENT_NODE) {
                                      const role = el.getAttribute('role');
                                      if (role === 'textbox' || role === 'searchbox' || role === 'combobox') return true;
                                  }
                              }

                              function getDescription(el) {
                                  function getGeneralDescription() {
                                      return (
                                          el.getAttribute('aria-label') ||
                                          el.getAttribute('title') ||
                                          ''
                                      ).trim();
                                  }

                                  if (el.nodeType === Node.TEXT_NODE) return el.textContent.trim();

                                  if (el.nodeType !== Node.ELEMENT_NODE) {
                                      return '';
                                  }

                                  // 输入框
                                  if (canEdit(el)) {
                                      const result = (
                                          el.value ||
                                          el.placeholder ||
                                          el.getAttribute('data-placeholder') ||
                                          ''
                                      ).trim()
                                      if (result)
                                          return result;
                                  }

                                  // 图片
                                  if (el.tagName === 'IMG') {
                                      const result = (
                                          el.alt ||
                                          el.className ||
                                          getGeneralDescription() ||
                                          el.tagName
                                      ).trim()
                                      if (result)
                                          return result;
                                  }

                                  return getGeneralDescription();
                              }

                              //重置id
                              document.querySelectorAll('[data-alife-id]')
                                  .forEach(el => el.removeAttribute('data-alife-id'));
                              let id = 0;

                              function getFullDescription(el, hadDescription = '') {
                                  if (!canVisible(el)) return '';

                                  //获取自身描述
                                  let selfDescription = getDescription(el).trim();
                                  //父元素可能包含子元素的描述
                                  if (hadDescription.includes(selfDescription)) {
                                      if (selfDescription.length > 4)
                                          selfDescription = '';
                                      else if (!selfDescription.match(/^[A-Za-z0-9]+$/))
                                          selfDescription = '';
                                  }

                                  //获取所有子元素描述
                                  let childrenDescription = '';
                                  for (const child of el.childNodes) {
                                      let childDescription = getFullDescription(child, hadDescription + selfDescription + childrenDescription).trim();
                                      if (childDescription === '')
                                          continue; //跳过不可见的子元素
                                      childrenDescription += childDescription + '-';
                                  }
                                  childrenDescription = childrenDescription.slice(0, -1);

                                  let description = (selfDescription + ' ' + childrenDescription).trim();
                                  //补齐交互信息
                                  if (canEdit(el) || canClick(el)) {
                                      el.setAttribute('data-alife-id', ++id);
                                      if (canEdit(el)) description = `[t${id}:${description}]`;
                                      else if (canClick(el)) description = `[b${id}:${description}]`;
                                  }

                                  return description
                              }

                              let fullDescription = getFullDescription(document.body);

                              const INPUT_MAX_PAGE_LENGTH = {{maxLength}};
                              const INPUT_PAGE_INDEX = {{page}};
                              let pageCount = Math.ceil(fullDescription.length / INPUT_MAX_PAGE_LENGTH);
                              let pageIndex = Math.min(Math.max(1, INPUT_PAGE_INDEX), pageCount);
                              let pageHint = `当前页码(${pageIndex}/${pageCount})` + (pageIndex < pageCount ? '可继续翻页' : '');
                              let pageDescription = fullDescription.slice(INPUT_MAX_PAGE_LENGTH * (pageIndex - 1), INPUT_MAX_PAGE_LENGTH * pageIndex)

                              return pageDescription + '\n\n' + pageHint;
                          })();
                          """;

        return await RunWebsiteJsAsync(jsCode);
    }
    public Task<string> GetElementInfoAsync(int id)
    {
        string jsCode = $$"""
                          (() => {
                              const el = document.querySelector('[data-alife-id="{{id}}"]');
                              return el ? el.outerHTML : '[元素不存在]';
                          })();
                          """;
        return RunWebsiteJsAsync(jsCode);
    }
    public Task<string> RunWebsiteJsAsync(string code)
    {
        return worker.ExecuteTaskAsync(async webView => {
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

    readonly BrowserWorker worker = new();

    public async ValueTask DisposeAsync()
    {
        await worker.DisposeAsync();
    }
}
