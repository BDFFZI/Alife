using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Skill;

public class SkillConfig
{
    public List<string> Blacklist { get; set; } = [];
}

public class SkillInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

[Module("Skill工具",
    "Skill 是一种渐进式（按需加载省token）的工具包，通过预编写的手册引导和规范AI完成各种各样的复杂任务。\n你可以使用\u201Cmodelscope skills add\u201D来手动添加新的技能。",
    defaultCategory: "Alife 官方/功能底座",
    EditorUI = typeof(SkillServiceUI))]
public class SkillService(
    XmlFunctionCaller functionService,
    IInteractor<SkillService> interactor) :
    ChatBehaviour,
    IConfigurable<SkillConfig>
{
    public SkillConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.OneShot)]
    public void StudySkill(string name)
    {
        string? skillDir = FindSkillDirectory(name);
        if (skillDir == null)
        {
            interactor.Poke($"[{nameof(StudySkill)}] skill \"{name}\" 不存在");
            return;
        }

        string skillDocPath = Path.Combine(skillDir, "SKILL.md");
        if (File.Exists(skillDocPath) == false)
        {
            interactor.Poke($"[{nameof(StudySkill)}] skill文件不存在");
            return;
        }

        string skillDoc = File.ReadAllText(skillDocPath);
        string[] appendFiles = Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories);

        interactor.Poke(
            $"""
             [{nameof(StudySkill)}] 已读取 {name} skill

             > 包含文件：
             - {string.Join("\n- ", appendFiles)}

             > 手册内容：
             ```
             {skillDoc}
             ```
             """);
    }

    readonly string skillsPath = Path.Combine(AlifePath.StorageFolderPath, "Skills");
    readonly Regex frontmatterRegex = new(@"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline);

    protected override Task OnAwake()
    {
        //获取所有skill并解析frontmatter
        List<SkillInfo> allSkills = [];
        if (Directory.Exists(skillsPath))
        {
            foreach (string dir in Directory.GetDirectories(skillsPath))
            {
                string name = Path.GetFileName(dir);
                string skillDocPath = Path.Combine(dir, "SKILL.md");
                SkillInfo info = new() { Name = name };
                if (File.Exists(skillDocPath))
                {
                    string content = File.ReadAllText(skillDocPath);
                    var (frontName, frontDesc) = ParseFrontmatter(content);
                    if (frontName != null) info.Name = frontName;
                    if (frontDesc != null) info.Description = frontDesc;
                }
                allSkills.Add(info);
            }
        }

        //黑名单过滤
        IEnumerable<SkillInfo> filtered = allSkills;
        if (Configuration.Blacklist.Count > 0)
        {
            HashSet<string> blacklist = new(Configuration.Blacklist);
            filtered = allSkills.Where(s => !blacklist.Contains(s.Name));
        }

        //构建skill列表文本
        string[] skillLines = filtered
            .Select(s => string.IsNullOrEmpty(s.Description) ? s.Name : $"{s.Name} - {s.Description}")
            .ToArray();

        //注册函数
        XmlHandler xmlHandler = new(this) {
            Explanation = $$"""
                            已有Skill
                            - {{(skillLines.Length == 0 ? "无Skill" : string.Join("\n- ", skillLines))}}

                            创建Skill
                            在`{{skillsPath}}`目录下存放一个`{Skill名称}/SKILL.md`即可被识别为Skill，然后你可以在此基础上增加额外的脚本文件等，具体可以参考其中或网络上常见的Skill写法
                            """
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        return Task.CompletedTask;
    }

    string? FindSkillDirectory(string name)
    {
        if (!Directory.Exists(skillsPath)) return null;

        foreach (string dir in Directory.GetDirectories(skillsPath))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName == name) return dir;

            string skillDocPath = Path.Combine(dir, "SKILL.md");
            if (File.Exists(skillDocPath))
            {
                string content = File.ReadAllText(skillDocPath);
                var (frontName, _) = ParseFrontmatter(content);
                if (frontName == name) return dir;
            }
        }

        return null;
    }
    (string? name, string? description) ParseFrontmatter(string content)
    {
        Match match = frontmatterRegex.Match(content);
        if (!match.Success) return (null, null);

        string yaml = match.Groups[1].Value;
        string? name = null;
        string? description = null;

        foreach (string line in yaml.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("name:"))
                name = trimmed["name:".Length..].Trim().Trim('"', '\'');
            else if (trimmed.StartsWith("description:"))
                description = trimmed["description:".Length..].Trim().Trim('"', '\'');
        }

        return (name, description);
    }
}
