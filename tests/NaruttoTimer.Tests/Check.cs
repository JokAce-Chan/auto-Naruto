namespace NaruttoTimer.Tests;

/// <summary>极简断言工具（零依赖测试框架）。</summary>
public static class Check
{
    public static int Passed;
    public static int Failed;

    private static void Ok() => Passed++;

    public static void True(bool condition, string message = "期望为真")
    {
        if (!condition) throw new Exception($"断言失败：{message}");
        Ok();
    }

    public static void False(bool condition, string message = "期望为假")
    {
        if (condition) throw new Exception($"断言失败：{message}");
        Ok();
    }

    public static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"断言失败：{message}，期望 {expected}，实际 {actual}");
        Ok();
    }

    public static void NotEqual<T>(T unexpected, T actual, string message = "")
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new Exception($"断言失败：{message}，不应等于 {actual}");
        Ok();
    }

    public static void Near(double expected, double actual, double tolerance = 0.01, string message = "")
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception($"断言失败：{message}，期望 {expected}±{tolerance}，实际 {actual}");
        Ok();
    }

    public static T Throws<T>(Action action, string message = "") where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            Ok();
            return ex;
        }
        throw new Exception($"断言失败：{message}，未抛出 {typeof(T).Name}");
    }
}
