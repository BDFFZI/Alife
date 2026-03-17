using OpenCvSharp;
using System.Text;

namespace Alife.Vision;

public class ImageAnalyzer
{
    /// <summary>
    /// 将图片转换为灰度图
    /// </summary>
    public void MakeGrayScale(string inputPath, string outputPath)
    {
        using var src = new Mat(inputPath, ImreadModes.Color);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        gray.SaveImage(outputPath);
    }

    /// <summary>
    /// 检测人脸并绘制矩形框
    /// </summary>
    public int DetectAndDrawFaces(string inputPath, string outputPath, string cascadePath)
    {
        using var src = new Mat(inputPath, ImreadModes.Color);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        using var cascade = new CascadeClassifier(cascadePath);
        var faces = cascade.DetectMultiScale(
            image: gray,
            scaleFactor: 1.1,
            minNeighbors: 3,
            flags: HaarDetectionTypes.ScaleImage,
            minSize: new Size(30, 30)
        );

        foreach (var rect in faces)
        {
            Cv2.Rectangle(src, rect, Scalar.Red, 2);
        }

        src.SaveImage(outputPath);
        return faces.Length;
    }

    /// <summary>
    /// 简单的边缘检测
    /// </summary>
    public void DetectEdges(string inputPath, string outputPath)
    {
        using var src = new Mat(inputPath, ImreadModes.Color);
        using var gray = new Mat();
        using var edges = new Mat();
        
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Canny(gray, edges, 50, 150);
        edges.SaveImage(outputPath);
    }

    /// <summary>
    /// 获取图片的语义标签信息
    /// </summary>
    public List<string> GetSemanticTags(string inputPath, Dictionary<string, string> cascadePaths)
    {
        var tags = new List<string>();
        using var src = new Mat(inputPath, ImreadModes.Color);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // 1. 亮度分析
        Scalar mean = Cv2.Mean(gray);
        double brightness = mean.Val0;
        if (brightness < 60) tags.Add("环境：昏暗");
        else if (brightness > 190) tags.Add("环境：明亮");
        else tags.Add("环境：光线适中");

        // 2. 复杂度分析 (边缘密度)
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 100, 200);
        double nonZeroCount = Cv2.CountNonZero(edges);
        double totalPixels = edges.Width * edges.Height;
        double density = (nonZeroCount / totalPixels) * 100;
        if (density > 5) tags.Add("复杂度：高(细节丰富)");
        else if (density < 1) tags.Add("复杂度：低(画面简洁)");
        else tags.Add("复杂度：适中");

        // 3. 色彩倾向
        Scalar colorMean = Cv2.Mean(src);
        double b = colorMean.Val0;
        double g = colorMean.Val1;
        double r = colorMean.Val2;
        if (r > g && r > b) tags.Add("主要色调：偏红/暖色");
        else if (g > r && g > b) tags.Add("主要色调：偏绿");
        else if (b > r && b > g) tags.Add("主要色调：偏蓝/冷色");

        // 4. 特征检测
        foreach (var kvp in cascadePaths)
        {
            string featureName = kvp.Key;
            string path = kvp.Value;
            if (File.Exists(path))
            {
                using var cascade = new CascadeClassifier(path);
                var items = cascade.DetectMultiScale(gray, 1.1, 3);
                if (items.Length > 0)
                {
                    tags.Add($"检测到 {featureName} x{items.Length}");
                }
            }
        }

        return tags;
    }
}
