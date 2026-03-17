using OpenCvSharp;

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
}
