using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PwshCompiler;

/// <summary>
/// PowerShell-to-C# compiler using PowerShell SDK's AST parser and Roslyn for C# generation.
/// In the GenerateCSharp method, Roslyn is used to construct the C# code dynamically
/// Parses &lt;script type="pwsh"&gt; blocks from HTML and generates equivalent C# code.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PwshCompiler <htmlPath> <outputPath>");
            return 1;
        }

        var htmlPath = args[0];
        var outputPath = args[1];

        try
        {
            var compiler = new PowerShellToCSharpCompiler();
            var code = compiler.CompileFromHtml(htmlPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, code, Encoding.UTF8);
            Console.WriteLine($"Generated: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PowerShellToCSharpCompiler
{
    private readonly Dictionary<string, (Dictionary<string, string> Params, Dictionary<string, string> EnvRefs)> _hashtables = new(StringComparer.OrdinalIgnoreCase);

    public string CompileFromHtml(string htmlPath)
    {
        var psBlocks = ExtractPowerShellBlocks(htmlPath);
        return GenerateCSharp(psBlocks);
    }

    private List<string> ExtractPowerShellBlocks(string htmlPath)
    {
        var blocks = new List<string>();

        if (!File.Exists(htmlPath))
        {
            Console.WriteLine($"Warning: {htmlPath} not found");
            return blocks;
        }

        var html = File.ReadAllText(htmlPath);
        var matches = Regex.Matches(html, @"<script\s+type=""pwsh""[^>]*>(?<code>.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var code = m.Groups["code"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(code))
            {
                blocks.Add(code);
            }
        }

        return blocks;
    }

    private string GenerateCSharp(List<string> psBlocks)
    {
        var statements = new List<StatementSyntax>();

        // var outputs = new List<string>();
        statements.Add(LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator("outputs")
                        .WithInitializer(EqualsValueClause(
                            ObjectCreationExpression(GenericName("List")
                                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(
                                    PredefinedType(Token(SyntaxKind.StringKeyword))))))
                            .WithArgumentList(ArgumentList())))))));

        foreach (var block in psBlocks)
        {
            var blockStatements = CompilePowerShellBlock(block);
            statements.AddRange(blockStatements);
        }

        // Return statement
        statements.Add(IfStatement(
            BinaryExpression(SyntaxKind.EqualsExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("outputs"), IdentifierName("Count")),
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
            ReturnStatement(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(SyntaxKind.StringKeyword)), IdentifierName("Empty")))));

        statements.Add(ReturnStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(SyntaxKind.StringKeyword)), IdentifierName("Join")))
            .WithArgumentList(ArgumentList(SeparatedList(new[]
            {
                Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Environment"), IdentifierName("NewLine"))),
                Argument(IdentifierName("outputs"))
            })))));

        var executeMethod = MethodDeclaration(
            GenericName("Task").WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(
                PredefinedType(Token(SyntaxKind.StringKeyword))))),
            "ExecuteAsync")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword)))
            .WithBody(Block(statements));

        var helperMethods = GenerateHelperMethods();

        var classDecl = ClassDeclaration("CompiledPowerShell")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.PartialKeyword)))
            .WithMembers(List<MemberDeclarationSyntax>(new MemberDeclarationSyntax[] { executeMethod }.Concat(helperMethods)));

        var namespaceDecl = FileScopedNamespaceDeclaration(IdentifierName("PsWasmApp"))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(classDecl));

        var usings = new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Threading.Tasks",
            "System.Text.RegularExpressions"
        }.Select(u => UsingDirective(ParseName(u)));

        var compilationUnit = CompilationUnit()
            .WithUsings(List(usings))
            .AddMembers(namespaceDecl)
            .NormalizeWhitespace();

        var header = "// Auto-generated from inline PowerShell (index.html) at build time\n#nullable enable\n";
        return header + compilationUnit.ToFullString();
    }

    private List<StatementSyntax> CompilePowerShellBlock(string code)
    {
        var statements = new List<StatementSyntax>();

        // Parse PowerShell AST
        var ast = Parser.ParseInput(code, out var tokens, out var errors);

        if (errors != null && errors.Length > 0)
        {
            Console.WriteLine($"Warning: Parse errors in PowerShell block: {errors[0].Message}");
            return statements;
        }

        // Create block: { var blockOutputs = new List<string>(); ... outputs.Add(...); }
        var blockStatements = new List<StatementSyntax>();

        // var blockOutputs = new List<string>();
        blockStatements.Add(LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator("blockOutputs")
                        .WithInitializer(EqualsValueClause(
                            ObjectCreationExpression(GenericName("List")
                                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(
                                    PredefinedType(Token(SyntaxKind.StringKeyword))))))
                            .WithArgumentList(ArgumentList())))))));

        _hashtables.Clear();

        // First pass: collect hashtable assignments
        if (ast.EndBlock?.Statements != null)
        {
            foreach (var stmt in ast.EndBlock.Statements)
            {
                if (stmt is AssignmentStatementAst assign)
                {
                    ProcessHashtableAssignment(assign);
                }
            }
        }

        // Second pass: process commands
        if (ast.EndBlock?.Statements != null)
        {
            foreach (var stmt in ast.EndBlock.Statements)
            {
                var compiled = CompileStatement(stmt);
                if (compiled != null)
                {
                    blockStatements.AddRange(compiled);
                }
            }
        }

        // outputs.Add(string.Join(Environment.NewLine, blockOutputs));
        blockStatements.Add(ExpressionStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("outputs"), IdentifierName("Add")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    PredefinedType(Token(SyntaxKind.StringKeyword)), IdentifierName("Join")))
                .WithArgumentList(ArgumentList(SeparatedList(new[]
                {
                    Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Environment"), IdentifierName("NewLine"))),
                    Argument(IdentifierName("blockOutputs"))
                })))))))));

        statements.Add(Block(blockStatements));
        return statements;
    }

    private void ProcessHashtableAssignment(AssignmentStatementAst assign)
    {
        ExpressionAst? expr = null;
        if (assign.Right is CommandExpressionAst cea)
        {
            expr = cea.Expression;
        }
        else if (assign.Right is PipelineAst pipe && pipe.PipelineElements.Count == 1 &&
                 pipe.PipelineElements[0] is CommandExpressionAst cea2)
        {
            expr = cea2.Expression;
        }

        if (expr is HashtableAst hta)
        {
            var varName = assign.Left switch
            {
                VariableExpressionAst v => v.VariablePath.UserPath,
                _ => assign.Left.Extent.Text.TrimStart('$')
            };

            var (paramMap, envRefs) = ConvertHashtable(hta);
            _hashtables[varName] = (paramMap, envRefs);
        }
    }

    private List<StatementSyntax>? CompileStatement(StatementAst stmt)
    {
        if (stmt is PipelineAst pipe && pipe.PipelineElements.Count == 1 &&
            pipe.PipelineElements[0] is CommandAst cmd)
        {
            var name = cmd.GetCommandName();
            if (string.IsNullOrEmpty(name)) return null;

            // Handle Write-Output
            if (name.Equals("Write-Output", StringComparison.OrdinalIgnoreCase))
            {
                return CompileWriteOutput(cmd);
            }

            // Handle Read-AzCosmosItems
            if (name.Equals("Read-AzCosmosItems", StringComparison.OrdinalIgnoreCase))
            {
                return CompileReadAzCosmosItems(cmd);
            }
        }

        return null;
    }

    private List<StatementSyntax> CompileWriteOutput(CommandAst cmd)
    {
        var statements = new List<StatementSyntax>();

        var arg = cmd.CommandElements.Skip(1).FirstOrDefault(e => e is not CommandParameterAst) as ExpressionAst;
        var message = ResolveScalarValue(arg, "write-output", new Dictionary<string, string>());

        if (!string.IsNullOrEmpty(message))
        {
            // blockOutputs.Add("message");
            statements.Add(ExpressionStatement(
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("blockOutputs"), IdentifierName("Add")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                    LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(message))))))));
        }

        return statements;
    }

    private List<StatementSyntax> CompileReadAzCosmosItems(CommandAst cmd)
    {
        var statements = new List<StatementSyntax>();
        var (paramInfo, envRefs) = CollectCommandParameters(cmd);

        paramInfo.TryGetValue("databasename", out var databaseName);
        paramInfo.TryGetValue("containername", out var containerName);
        paramInfo.TryGetValue("accountname", out var accountName);

        var top = paramInfo.TryGetValue("top", out var topStr) && int.TryParse(topStr, out var topVal) ? topVal : 1;
        var query = paramInfo.TryGetValue("query", out var queryStr) ? queryStr : $"SELECT TOP {top} * FROM c";
        var partitionKey = paramInfo.TryGetValue("partitionkey", out var pk) ? pk : string.Empty;

        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(containerName))
        {
            statements.Add(ExpressionStatement(
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("blockOutputs"), IdentifierName("Add")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                    LiteralExpression(SyntaxKind.StringLiteralExpression,
                        Literal("Cosmos parameters missing."))))))));
            return statements;
        }

        string? envVar = null;
        var isDynamic = false;

        if (envRefs.TryGetValue("accountkey", out var envRef))
        {
            envVar = envRef;
            isDynamic = true;
        }
        else if (paramInfo.TryGetValue("accountkey", out var accountKey) && IsEnvIdentifier(accountKey))
        {
            envVar = accountKey;
            isDynamic = true;
        }

        if (isDynamic && !string.IsNullOrWhiteSpace(accountName) && !string.IsNullOrWhiteSpace(envVar))
        {
            statements.AddRange(GenerateDynamicCosmosCall(accountName, databaseName!, containerName!, query, partitionKey, envVar));
        }
        else if (!string.IsNullOrWhiteSpace(accountName) && paramInfo.TryGetValue("accountkey", out var staticKey) && !string.IsNullOrWhiteSpace(staticKey))
        {
            if (staticKey.StartsWith("="))
            {
                staticKey = staticKey.TrimStart('=');
            }
            var endpoint = $"https://{accountName}.documents.azure.com:443/";
            var connectionString = $"AccountEndpoint={endpoint};AccountKey={staticKey};";
            statements.AddRange(GenerateStaticCosmosCall(connectionString, databaseName!, containerName!, query, partitionKey));
        }
        else if (paramInfo.TryGetValue("connectionstring", out var connStr) && !string.IsNullOrWhiteSpace(connStr))
        {
            statements.AddRange(GenerateStaticCosmosCall(connStr, databaseName!, containerName!, query, partitionKey));
        }
        else
        {
            statements.Add(ExpressionStatement(
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("blockOutputs"), IdentifierName("Add")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                    LiteralExpression(SyntaxKind.StringLiteralExpression,
                        Literal("Cosmos AccountKey not provided (check environment variable)."))))))));
        }

        return statements;
    }

    private List<StatementSyntax> GenerateDynamicCosmosCall(string accountName, string databaseName, string containerName, string query, string partitionKey, string envVar)
    {
        // Generate code like:
        // string? accountKey = Environment.GetEnvironmentVariable("envVar");
        // if (string.IsNullOrWhiteSpace(accountKey)) { accountKey = BuildSecrets.CosmosKey; }
        // if (string.IsNullOrWhiteSpace(accountKey)) { blockOutputs.Add("..."); }
        // else { ... call ReadFirstCosmosItemViaRestAsync ... }

        var stmts = new List<StatementSyntax>();

        // string? accountKey = Environment.GetEnvironmentVariable("envVar");
        stmts.Add(LocalDeclarationStatement(
            VariableDeclaration(NullableType(PredefinedType(Token(SyntaxKind.StringKeyword))))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator("accountKey")
                        .WithInitializer(EqualsValueClause(
                            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("Environment"), IdentifierName("GetEnvironmentVariable")))
                            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                                LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(envVar))))))))))));

        // if (string.IsNullOrWhiteSpace(accountKey)) { accountKey = BuildSecrets.CosmosKey; }
        stmts.Add(IfStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(SyntaxKind.StringKeyword)), IdentifierName("IsNullOrWhiteSpace")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("accountKey"))))),
            Block(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                IdentifierName("accountKey"),
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("BuildSecrets"), IdentifierName("CosmosKey")))))));

        // if (string.IsNullOrWhiteSpace(accountKey)) { blockOutputs.Add("..."); } else { ... }
        var errorStmt = ExpressionStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("blockOutputs"), IdentifierName("Add")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                LiteralExpression(SyntaxKind.StringLiteralExpression,
                    Literal($"Environment variable {envVar} not set.")))))));

        var elseStatements = new List<StatementSyntax>();

        // if (accountKey.StartsWith("=")) { accountKey = accountKey.TrimStart('='); }
        elseStatements.Add(IfStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("accountKey"), IdentifierName("StartsWith")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("=")))))),
            Block(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                IdentifierName("accountKey"),
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("accountKey"), IdentifierName("TrimStart")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                    LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('=')))))))))));

        // string endpoint = "https://...";
        elseStatements.Add(LocalDeclarationStatement(
            VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator("endpoint")
                        .WithInitializer(EqualsValueClause(
                            LiteralExpression(SyntaxKind.StringLiteralExpression,
                                Literal($"https://{accountName}.documents.azure.com:443/"))))))));

        // string connectionString = $"AccountEndpoint={endpoint};AccountKey={accountKey};";
        elseStatements.Add(LocalDeclarationStatement(
            VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator("connectionString")
                        .WithInitializer(EqualsValueClause(
                            InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken))
                                .WithContents(List(new InterpolatedStringContentSyntax[]
                                {
                                    InterpolatedStringText().WithTextToken(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, "AccountEndpoint=", "AccountEndpoint=", TriviaList())),
                                    Interpolation(IdentifierName("endpoint")),
                                    InterpolatedStringText().WithTextToken(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, ";AccountKey=", ";AccountKey=", TriviaList())),
                                    Interpolation(IdentifierName("accountKey")),
                                    InterpolatedStringText().WithTextToken(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, ";", ";", TriviaList()))
                                }))))))));

        // blockOutputs.Add(await ReadFirstCosmosItemViaRestAsync(...));
        elseStatements.Add(ExpressionStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("blockOutputs"), IdentifierName("Add")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                AwaitExpression(
                    InvocationExpression(IdentifierName("ReadFirstCosmosItemViaRestAsync"))
                    .WithArgumentList(ArgumentList(SeparatedList(new[]
                    {
                        Argument(IdentifierName("connectionString")).WithNameColon(NameColon("connectionString")),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(databaseName))).WithNameColon(NameColon("databaseName")),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(containerName))).WithNameColon(NameColon("containerName")),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(query))).WithNameColon(NameColon("query")),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(partitionKey))).WithNameColon(NameColon("partitionKey"))
                    }))))))))));

        stmts.Add(IfStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(SyntaxKind.StringKeyword)), IdentifierName("IsNullOrWhiteSpace")))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("accountKey"))))),
            Block(errorStmt),
            ElseClause(Block(elseStatements))));

        return stmts;
    }

    private List<StatementSyntax> GenerateStaticCosmosCall(string connectionString, string databaseName, string containerName, string query, string partitionKey)
    {
        // blockOutputs.Add(await ReadFirstCosmosItemViaRestAsync(...));
        return new List<StatementSyntax>
        {
            ExpressionStatement(
                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("blockOutputs"), IdentifierName("Add")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                    AwaitExpression(
                        InvocationExpression(IdentifierName("ReadFirstCosmosItemViaRestAsync"))
                        .WithArgumentList(ArgumentList(SeparatedList(new[]
                        {
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(connectionString))).WithNameColon(NameColon("connectionString")),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(databaseName))).WithNameColon(NameColon("databaseName")),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(containerName))).WithNameColon(NameColon("containerName")),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(query))).WithNameColon(NameColon("query")),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(partitionKey))).WithNameColon(NameColon("partitionKey"))
                        })))))))))
        };
    }

    private (Dictionary<string, string> Params, Dictionary<string, string> EnvRefs) CollectCommandParameters(CommandAst cmd)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var elements = cmd.CommandElements;
        for (int i = 1; i < elements.Count; i++)
        {
            var el = elements[i];

            if (el is CommandParameterAst p)
            {
                var name = p.ParameterName?.ToLowerInvariant();
                if (string.IsNullOrEmpty(name)) continue;

                ExpressionAst? arg = p.Argument;
                if (arg == null && i + 1 < elements.Count && elements[i + 1] is not CommandParameterAst)
                {
                    arg = elements[i + 1] as ExpressionAst;
                    i++;
                }
                map[name] = arg == null ? "true" : (ResolveScalarValue(arg, name, envRefs) ?? string.Empty);
                continue;
            }

            if (el is VariableExpressionAst v && v.Splatted)
            {
                var name = v.VariablePath.UserPath;
                if (_hashtables.TryGetValue(name, out var ht))
                {
                    foreach (var kvp in ht.Params) map[kvp.Key] = kvp.Value;
                    foreach (var kvp in ht.EnvRefs) envRefs[kvp.Key] = kvp.Value;
                }
                continue;
            }

            if (el is HashtableAst hta && el.Extent.Text.StartsWith("@"))
            {
                var ht = ConvertHashtable(hta);
                foreach (var kvp in ht.Params) map[kvp.Key] = kvp.Value;
                foreach (var kvp in ht.EnvRefs) envRefs[kvp.Key] = kvp.Value;
            }
        }

        return (map, envRefs);
    }

    private (Dictionary<string, string> Params, Dictionary<string, string> EnvRefs) ConvertHashtable(HashtableAst hta)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in hta.KeyValuePairs)
        {
            var keyAst = kv.Item1;
            var valAst = kv.Item2;
            var key = keyAst is StringConstantExpressionAst s ? s.Value : keyAst.Extent.Text;
            var lower = key.ToLowerInvariant();
            var value = ResolveScalarValue(valAst, lower, envRefs) ?? string.Empty;

            if (string.IsNullOrEmpty(value) && valAst.Extent.Text.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            {
                var envName = valAst.Extent.Text.Substring(5);
                envRefs[lower] = envName;
                value = envName;
            }
            map[lower] = value;
        }

        return (map, envRefs);
    }

    private string? ResolveScalarValue(Ast? ast, string paramName, Dictionary<string, string> envRefs)
    {
        if (ast == null) return null;

        ExpressionAst? expr = ast as ExpressionAst;
        if (ast is CommandExpressionAst cea) expr = cea.Expression;
        if (ast is PipelineAst pa && pa.PipelineElements.Count == 1 && pa.PipelineElements[0] is CommandExpressionAst cea2)
            expr = cea2.Expression;

        if (expr == null) return null;

        return expr switch
        {
            StringConstantExpressionAst s => s.Value,
            ConstantExpressionAst c => c.Value?.ToString(),
            ExpandableStringExpressionAst es => es.Value,
            VariableExpressionAst v when v.VariablePath.IsDriveQualified &&
                v.VariablePath.DriveName.Equals("env", StringComparison.OrdinalIgnoreCase) =>
                HandleEnvVariable(v, paramName, envRefs),
            VariableExpressionAst v => v.Extent.Text.Trim('"', '\''),
            _ => expr.Extent.Text.Trim('"', '\'')
        };
    }

    private string HandleEnvVariable(VariableExpressionAst v, string paramName, Dictionary<string, string> envRefs)
    {
        // UserPath includes the drive, e.g. "env:VAR_NAME", so we extract just the variable name
        var userPath = v.VariablePath.UserPath;
        var envName = userPath.Contains(':') ? userPath.Substring(userPath.IndexOf(':') + 1) : userPath;
        envRefs[paramName] = envName;
        var val = Environment.GetEnvironmentVariable(envName);
        return string.IsNullOrEmpty(val) ? envName : val;
    }

    private static bool IsEnvIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$");

    private List<MemberDeclarationSyntax> GenerateHelperMethods()
    {
        // Generate helper methods by parsing raw C# code - much cleaner than building via SyntaxFactory
        var helperCode = @"
    private static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient();

    private static async System.Threading.Tasks.Task<string> ReadFirstCosmosItemViaRestAsync(string connectionString, string databaseName, string containerName, string query, string? partitionKey)
    {
        try
        {
            if (!TryParseConnectionString(connectionString, out var endpoint, out var key))
            {
                return ""Invalid Cosmos connection string."";
            }

            var baseUri = new System.Uri(endpoint);
            var resourceLink = $""dbs/{databaseName}/colls/{containerName}"";
            var date = System.DateTime.UtcNow.ToString(""r"", System.Globalization.CultureInfo.InvariantCulture);

            const string verb = ""post"";
            var httpMethod = System.Net.Http.HttpMethod.Post;
            var auth = BuildAuthToken(verb, ""docs"", resourceLink, date, key);
            var requestUri = new System.Uri(baseUri, $""{resourceLink}/docs"").ToString();

            var payload = BuildQueryPayload(query);

            using var message = new System.Net.Http.HttpRequestMessage(httpMethod, requestUri);
            message.Headers.TryAddWithoutValidation(""x-ms-date"", date);
            message.Headers.TryAddWithoutValidation(""x-ms-version"", ""2018-12-31"");
            message.Headers.TryAddWithoutValidation(""Authorization"", auth);
            message.Headers.TryAddWithoutValidation(""x-ms-documentdb-isquery"", ""true"");
            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                message.Headers.TryAddWithoutValidation(""x-ms-documentdb-query-enablecrosspartition"", ""true"");
            }
            else
            {
                var escapedPk = System.Text.Json.JsonEncodedText.Encode(partitionKey);
                message.Headers.TryAddWithoutValidation(""x-ms-documentdb-partitionkey"", $""[\""{escapedPk.ToString()}\""]"");
            }
            message.Headers.TryAddWithoutValidation(""Accept"", ""application/json"");
            message.Content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, ""application/query+json"");
            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(""application/query+json"");

            var response = await Http.SendAsync(message);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $""Cosmos REST error {(int)response.StatusCode}: {response.ReasonPhrase} {content}"";
            }

            using var doc = System.Text.Json.JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty(""Documents"", out var docs) || docs.GetArrayLength() == 0)
            {
                return ""No items found."";
            }

            return docs[0].GetRawText();
        }
        catch (System.Exception ex)
        {
            return $""Failed to read Cosmos DB item via REST: {ex.Message}"";
        }
    }

    private static bool TryParseConnectionString(string connectionString, out string endpoint, out string key)
    {
        endpoint = string.Empty;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var parts = connectionString.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var name = kv[0].Trim();
            var value = kv[1].Trim();

            if (name.Equals(""AccountEndpoint"", System.StringComparison.OrdinalIgnoreCase))
            {
                endpoint = value;
            }
            else if (name.Equals(""AccountKey"", System.StringComparison.OrdinalIgnoreCase))
            {
                key = value;
            }
        }

        return !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key);
    }

    private static string BuildAuthToken(string verb, string resourceType, string resourceLink, string date, string key)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(verb.ToLowerInvariant());
        sb.Append('\n');
        sb.Append(resourceType.ToLowerInvariant());
        sb.Append('\n');
        sb.Append(resourceLink);
        sb.Append('\n');
        sb.Append(date.ToLowerInvariant());
        sb.Append('\n');
        sb.Append('\n');
        var payload = sb.ToString();

        var keyBytes = System.Convert.FromBase64String(key);
        using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = System.Convert.ToBase64String(hash);
        var auth = $""type=master&ver=1.0&sig={signature}"";

        return System.Uri.EscapeDataString(auth);
    }

    private static string BuildQueryPayload(string query)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(""query"", query);
            writer.WriteStartArray(""parameters"");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
";
        // Parse the helper methods and extract member declarations
        var tree = CSharpSyntaxTree.ParseText($"class Temp {{ {helperCode} }}");
        var root = tree.GetCompilationUnitRoot();
        var tempClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        return tempClass.Members.ToList();
    }
}
