using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string directory = Path.GetFullPath(args[0]);
var files = Directory.GetFiles(directory, "*.cs");
foreach (string file in files)
{
    var source = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
    if (source.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        throw new InvalidOperationException($"Invalid C# source: {file}");

    string formatted = source.GetRoot().NormalizeWhitespace(indentation: "    ", eol: Environment.NewLine).ToFullString();
    var normalized = CSharpSyntaxTree.ParseText(formatted);
    var initializers = normalized.GetRoot().DescendantNodes()
        .OfType<InitializerExpressionSyntax>()
        .Where(initializer => initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
            && initializer.Expressions.Count > 1
            && !initializer.DescendantNodes().OfType<InitializerExpressionSyntax>().Any())
        .OrderByDescending(initializer => initializer.SpanStart);

    foreach (var initializer in initializers)
    {
        int lineStart = formatted.LastIndexOf('\n', initializer.SpanStart) + 1;
        int indentation = formatted.Skip(lineStart).TakeWhile(character => character == ' ').Count();
        string outerIndent = new(' ', indentation);
        string innerIndent = outerIndent + "    ";
        string members = string.Join("," + Environment.NewLine, initializer.Expressions.Select(expression => innerIndent + expression.ToFullString().Trim()));
        string replacement = "{" + Environment.NewLine + members + Environment.NewLine + outerIndent + "}";
        formatted = formatted.Remove(initializer.SpanStart, initializer.Span.Length).Insert(initializer.SpanStart, replacement);
    }
    File.WriteAllText(file, formatted + Environment.NewLine);
}
Console.WriteLine($"Formatted {files.Length} C# files.");
