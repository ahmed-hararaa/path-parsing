using Harara.PathParsing.Net;

namespace Test;

public class DeepTestPathProxy : IPathProxy
{
    private readonly List<string> _expectedCommands;
    private readonly List<string> _actualCommands = new();

    public DeepTestPathProxy(List<string> expectedCommands)
    {
        _expectedCommands = expectedCommands ?? throw new ArgumentNullException(nameof(expectedCommands));
    }

    public void Close()
    {
        _actualCommands.Add("close()");
    }

    public void CubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _actualCommands.Add($"cubicTo({x1:F4}, {y1:F4}, {x2:F4}, {y2:F4}, {x3:F4}, {y3:F4})");
    }

    public void LineTo(double x, double y)
    {
        _actualCommands.Add($"lineTo({x:F4}, {y:F4})");
    }

    public void MoveTo(double x, double y)
    {
        _actualCommands.Add($"moveTo({x:F4}, {y:F4})");
    }

    public void Validate()
    {
        Assert.AreEqual(_expectedCommands, _actualCommands);
    }
}

public class ParsePathDeepTest
{
    [SetUp]
    public void Setup()
    {
    }

    private void AssertValidPath(string input, List<string> commands)
    {
        var proxy = new DeepTestPathProxy(commands);
        SvgPathParser.WriteSvgPathDataToPath(input, proxy);
        proxy.Validate();
    }

    [Test]
    public void DeepPathValidation()
    {
        
        AssertValidPath("M20,30 Q40,5 60,30 T100,30", new List<string>
        {
            "moveTo(20.0000, 30.0000)",
            "cubicTo(33.3333, 13.3333, 46.6667, 13.3333, 60.0000, 30.0000)",
            "cubicTo(73.3333, 46.6667, 86.6667, 46.6667, 100.0000, 30.0000)"
        });
        
        AssertValidPath("M5.5 5.5a.5 1.5 30 1 1-.866-.5.5 1.5 30 1 1 .866.5z", new List<string>
        {
            "moveTo(5.5000, 5.5000)",
            "cubicTo(5.2319, 5.9667, 4.9001, 6.3513, 4.6307, 6.5077)",
            "cubicTo(4.3612, 6.6640, 4.1953, 6.5683, 4.1960, 6.2567)",
            "cubicTo(4.1967, 5.9451, 4.3638, 5.4655, 4.6340, 5.0000)",
            "cubicTo(4.9021, 4.5333, 5.2339, 4.1487, 5.5033, 3.9923)",
            "cubicTo(5.7728, 3.8360, 5.9387, 3.9317, 5.9380, 4.2433)",
            "cubicTo(5.9373, 4.5549, 5.7702, 5.0345, 5.5000, 5.5000)",
            "close()"
        });
    }
}