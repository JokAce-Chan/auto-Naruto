using System.Reflection;

namespace NaruttoTimer.Tests;

/// <summary>通过反射发现并运行所有 [Fact] 测试。</summary>
public static class TestRunner
{
    public static int RunAll()
    {
        var methods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
            .OrderBy(m => m.DeclaringType!.Name)
            .ThenBy(m => m.Name)
            .ToList();

        Console.WriteLine($"发现 {methods.Count} 个测试");
        var failures = new List<string>();

        foreach (var m in methods)
        {
            try
            {
                m.Invoke(null, null);
                Console.WriteLine($"[PASS] {m.DeclaringType!.Name}.{m.Name}");
            }
            catch (TargetInvocationException tie)
            {
                var ex = tie.InnerException ?? tie;
                failures.Add($"{m.DeclaringType!.Name}.{m.Name}: {ex.Message}");
                Console.WriteLine($"[FAIL] {m.DeclaringType!.Name}.{m.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"{m.DeclaringType!.Name}.{m.Name}: {ex.Message}");
                Console.WriteLine($"[FAIL] {m.DeclaringType!.Name}.{m.Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"结果：通过 {Check.Passed} 个断言，失败 {Check.Failed} 个，测试失败 {failures.Count} 个");
        return failures.Count == 0 ? 0 : 1;
    }
}
