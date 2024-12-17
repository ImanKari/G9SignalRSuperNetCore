using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
///     MSBuild task to generate SignalR client-side helper code for G9SignalRSuperNetCore hubs.
/// </summary>
// ReSharper disable once UnusedMember.Global
// ReSharper disable once CheckNamespace
public class G9HubClientGeneratorTask : Task
{
    /// <summary>
    ///     Gets or sets the input directory containing the source files to process.
    /// </summary>
    [Required]
    public string InputDirectory { get; set; }

    /// <summary>
    ///     Gets or sets the output file path for the generated client helper code.
    /// </summary>
    [Required]
    public string OutputFile { get; set; }

    /// <summary>
    ///     Executes the task to generate client-side helper code.
    /// </summary>
    /// <returns>Returns true if the task executes successfully; otherwise, false.</returns>
    public override bool Execute()
    {
        try
        {
            var generatedContent = new StringBuilder();

            // Add necessary using directives for the generated code.
            var necessaryReference = @"using G9SignalRSuperNetCore.Client;";

            // Process all .cs files in the input directory.
            foreach (var filePath in Directory.GetFiles(InputDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath));
                var root = syntaxTree.GetRoot();

                // Find all classes inheriting from G9AHubBase.
                var hubClasses = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Where(cls => cls.BaseList != null && cls.BaseList.Types
                        .Any(baseType => baseType.ToString().Contains("G9AHubBase")));

                foreach (var hubClass in hubClasses)
                {
                    var className = hubClass.Identifier.Text;

                    // Extract the TClientSideMethodsInterface generic argument.
                    var clientInterface = ExtractClientInterface(hubClass);

                    // Extract methods annotated with G9AttrMapMethodForClient.
                    var serverMethods = ExtractAnnotatedMethods(hubClass);

                    // Generate the helper code for the current hub class.
                    var code = GenerateCode(className, clientInterface, serverMethods, hubClass);

                    generatedContent.AppendLine(code);
                }
            }

            // Write the generated code to the output file.
            File.WriteAllText(OutputFile, necessaryReference + generatedContent);
            Log.LogMessage(MessageImportance.High, $"Generated client helper file at: {OutputFile}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    /// <summary>
    ///     Extracts the second generic argument (TClientSideMethodsInterface) from the G9AHubBase inheritance.
    /// </summary>
    /// <param name="cls">The class declaration syntax to process.</param>
    /// <returns>Returns the name of the client-side methods interface.</returns>
    private string ExtractClientInterface(ClassDeclarationSyntax cls)
    {
        if (cls.BaseList == null) return "UnknownInterface";

        var baseType = cls.BaseList.Types.First(bt => bt.ToString().Contains("G9AHubBase"));
        var genericArguments = baseType.ToString()
            .Split('<', '>', ',')
            .Skip(1)
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .Select(arg => arg.Trim())
            .ToList();

        return genericArguments.ElementAtOrDefault(1) ?? "UnknownInterface";
    }

    /// <summary>
    ///     Extracts methods from a class that are annotated with the G9AttrMapMethodForClient attribute.
    /// </summary>
    /// <param name="cls">The class declaration syntax to process.</param>
    /// <returns>Returns a list of method declarations annotated with G9AttrMapMethodForClient.</returns>
    private List<MethodDeclarationSyntax> ExtractAnnotatedMethods(ClassDeclarationSyntax cls)
    {
        return cls.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(m => m.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => attr.Name.ToString() == "G9AttrMapMethodForClient"))
            .ToList();
    }

    /// <summary>
    ///     Generates the client-side helper code, including interfaces and client classes.
    /// </summary>
    /// <param name="className">The name of the server-side hub class.</param>
    /// <param name="clientInterface">The name of the client-side methods interface.</param>
    /// <param name="serverMethods">The list of server-side methods.</param>
    /// <param name="hubClass">The class declaration syntax for the hub class.</param>
    /// <returns>Returns the generated code as a string.</returns>
    private string GenerateCode(string className, string clientInterface, List<MethodDeclarationSyntax> serverMethods,
        ClassDeclarationSyntax hubClass)
    {
        var methodsInterface = $"G9I{className}Methods";
        var listenersInterface = $"G9I{className}Listeners";
        var clientClass = $"G9C{className}Client";

        // Extract the RoutePattern value or fallback to className
        var routePattern = ExtractRoutePatternValue(hubClass, className);

        var listenersDefinition = GenerateInterfaceDefinition(clientInterface);
        var methodsDefinition = GenerateMethodsDefinition(serverMethods);

        return $@"
/* ---------------------------------------------------------------
 *       Auto-generated client helper for hub '{className}'
 * --------------------------------------------------------------- */

// Interface defining server-side methods callable from the client.
// Contains methods exposed by the server for client interaction.
public interface {methodsInterface}
{{
{methodsDefinition}
}}

// Interface defining client-side callback methods (listeners).
// Contains methods that the server can invoke on the client.
public interface {listenersInterface}
{{
{listenersDefinition}
}}

// Client class for hub '{className}'.
// Implements '{listenersInterface}' for handling server-side callbacks
// and provides methods to interact with the server via '{methodsInterface}'.
public class {clientClass} : G9SignalRSuperNetCoreClient<{clientClass}, {methodsInterface}, {listenersInterface}>, {listenersInterface}
{{
    public {clientClass}(string serverUrl)
        : base($""{{serverUrl}}{routePattern}"")
    {{
    }}
}}";
    }


    /// <summary>
    ///     Generates the definition for the listeners interface, including XML documentation comments if present.
    /// </summary>
    /// <param name="clientInterface">The name of the client interface.</param>
    /// <returns>Returns the listeners interface as a string with comments.</returns>
    private string GenerateInterfaceDefinition(string clientInterface)
    {
        var sb = new StringBuilder();

        // Locate the file containing the client interface
        var interfaceFile = Directory.GetFiles(InputDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)))
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .FirstOrDefault(i => i.Identifier.Text == clientInterface);

        if (interfaceFile == null)
        {
            sb.AppendLine($"    // Unable to locate client interface: {clientInterface}");
            return sb.ToString();
        }

        // Process each method in the client interface and include comments
        foreach (var method in interfaceFile.Members.OfType<MethodDeclarationSyntax>())
        {
            var xmlComment = ExtractXmlComment(method);

            var returnType = method.ReturnType.ToString();
            var parameters = string.Join(", ", method.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier}"));

            // Add XML comment (if any) and method signature
            if (!string.IsNullOrWhiteSpace(xmlComment)) sb.AppendLine(xmlComment);
            sb.AppendLine($"    {returnType} {method.Identifier}({parameters});");
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Generates the methods definition for the server-side methods interface,
    ///     including XML documentation comments if present.
    /// </summary>
    /// <param name="methods">The list of method declarations.</param>
    /// <returns>Returns the methods interface as a string with comments.</returns>
    private string GenerateMethodsDefinition(List<MethodDeclarationSyntax> methods)
    {
        var sb = new StringBuilder();

        foreach (var method in methods)
        {
            // Extract XML documentation comment if present
            var xmlComment = ExtractXmlComment(method);

            var returnType = method.ReturnType.ToString();
            var parameters = string.Join(", ", method.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier}"));

            // Add XML comment (if any) and method signature
            if (!string.IsNullOrWhiteSpace(xmlComment)) sb.AppendLine(xmlComment);
            sb.AppendLine($"    {returnType} {method.Identifier}({parameters});");
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Extracts XML documentation comments from a method declaration and formats them correctly.
    /// </summary>
    /// <param name="method">The method declaration syntax.</param>
    /// <returns>Returns the XML comments as a formatted string.</returns>
    private string ExtractXmlComment(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .Select(tr => tr.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (trivia == null) return null;

        var commentBuilder = new StringBuilder();

        // Process each node in the XML documentation
        foreach (var xmlNode in trivia.Content)
            switch (xmlNode)
            {
                case XmlElementSyntax element: // XML elements like <summary>, <param>, etc.
                    var elementName = element.StartTag.Name.ToString();

                    // Handle <param> specifically
                    if (elementName == "param")
                    {
                        var paramName = element.StartTag.Attributes
                            .OfType<XmlNameAttributeSyntax>()
                            .FirstOrDefault()?.Identifier.ToString();

                        var paramContent = string.Join(" ", element.Content
                            .OfType<XmlTextSyntax>()
                            .SelectMany(t => t.TextTokens)
                            .Select(t => t.Text.Trim())
                            .Where(t => !string.IsNullOrEmpty(t)));

                        commentBuilder.AppendLine($"    /// <param name=\"{paramName}\">{paramContent}</param>");
                    }
                    else // Handle general XML elements like <summary> and <returns>
                    {
                        commentBuilder.AppendLine($"    /// <{elementName}>");

                        foreach (var content in element.Content)
                            if (content is XmlTextSyntax xmlText)
                            {
                                var text = string.Join(" ", xmlText.TextTokens
                                    .Select(t => t.Text.Trim())
                                    .Where(t => !string.IsNullOrEmpty(t)));
                                commentBuilder.AppendLine($"    /// {text}");
                            }

                        commentBuilder.AppendLine($"    /// </{elementName}>");
                    }

                    break;
            }

        return commentBuilder.ToString();
    }

    /// <summary>
    ///     Extracts the value returned by the RoutePattern method in the hub class.
    ///     If the method is not implemented or returns invalid data, it falls back to the class name.
    /// </summary>
    /// <param name="hubClass">The class declaration syntax.</param>
    /// <param name="className">The name of the hub class.</param>
    /// <returns>The route pattern string or the class name as a fallback.</returns>
    private string ExtractRoutePatternValue(ClassDeclarationSyntax hubClass, string className)
    {
        var routePatternMethod = hubClass.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "RoutePattern" && m.ParameterList.Parameters.Count == 0);

        if (routePatternMethod == null) return className;

        // Look for the return statement in the method body
        var returnStatement = routePatternMethod.Body?.Statements
            .OfType<ReturnStatementSyntax>()
            .FirstOrDefault();

        if (returnStatement?.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
            return literal.Token.ValueText; // Return the string literal value

        // If no valid return value is found, fall back to class name
        return className;
    }
}