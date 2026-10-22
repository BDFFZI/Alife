namespace Alife.Function.Auditory;

/// <summary>兼容旧命名空间；新代码请使用 Alife.Function.AIModelUtility.IAuditoryModel。</summary>
public interface IAuditoryModel : global::Alife.Function.AIModelUtility.IAuditoryModel
{
}

public interface IAudioRecognizer : global::Alife.Function.AIModelUtility.IAudioRecognizer, IAuditoryModel
{
}

public interface IAudioRecognizerProvider : global::Alife.Function.AIModelUtility.IAudioRecognizerProvider
{
}
