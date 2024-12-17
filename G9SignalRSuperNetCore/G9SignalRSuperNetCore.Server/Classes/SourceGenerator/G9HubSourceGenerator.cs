using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public class G9HubSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        // Optional: Debug hook or setup
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Find all classes inheriting from G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        var classes = context.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(cls => cls.BaseList != null && cls.BaseList.Types
                .Any(baseType => baseType.ToString().Contains("G9AHubBase")));

        foreach (var cls in classes)
        {
            var className = cls.Identifier.Text;

            // Extract base type arguments
            var baseType = cls.BaseList.Types.First(bt => bt.ToString().Contains("G9AHubBase"));
            var typeArguments = ExtractTypeArguments(baseType);
            var clientInterface = typeArguments[1];

            // Generate interfaces and client class
            var generatedCode = GenerateCode(className, clientInterface);

            // Add generated code to compilation
            context.AddSource($"{className}Generated.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
        }
    }

    private List<string> ExtractTypeArguments(BaseTypeSyntax baseType)
    {
        var args = baseType.ToString()
            .Split('<', '>', ',')
            .Skip(1)
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .Select(arg => arg.Trim())
            .ToList();
        return args;
    }

    private string GenerateCode(string className, string clientInterface)
    {
        var methodsInterface = $"G9I{className}Methods";
        var listenersInterface = $"G9I{className}Listeners";
        var clientClass = $"G9C{className}Client";

        return $@"
using System.Threading.Tasks;

namespace GeneratedClients
{{
    // Methods interface
    public interface {methodsInterface}
    {{
        Task Login(string userName, string password);
    }}

    // Listeners interface
    public interface {listenersInterface} : {clientInterface}
    {{
    }}

    // Client class
    public class {clientClass} : G9SignalRSuperNetCoreClient<{methodsInterface}>, {listenersInterface}
    {{
        public {clientClass}(string serverUrl)
            : base($""{{serverUrl}}/{className}"")
        {{
            // Connection listeners (generated for the client interface)
            Connection.On<bool>(nameof(LoginResult), LoginResult);
        }}

        public Task LoginResult(bool accepted)
        {{
            return Task.CompletedTask;
        }}
    }}
}}";
    }
}