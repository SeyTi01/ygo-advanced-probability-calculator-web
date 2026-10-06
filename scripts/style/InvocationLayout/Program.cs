using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length == 0)
{
    return;
}

CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
foreach (string path in args)
{
    string source = File.ReadAllText(path, Encoding.UTF8);
    SyntaxNode root = CSharpSyntaxTree.ParseText(source, parseOptions, path).GetRoot();
    SyntaxNode formatted = new ForeachChainLayoutRewriter().Visit(root)!;
    string output = formatted.ToFullString();
    if (!string.Equals(source, output, StringComparison.Ordinal))
    {
        File.WriteAllText(path, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

internal sealed class ForeachChainLayoutRewriter : CSharpSyntaxRewriter
{
    private const string NewLine = "\n";

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        ForEachStatementSyntax visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        List<InvocationExpressionSyntax> calls = GetReceiverChain(visited.Expression);

        if (calls.Count < 2 ||
            !ContainsNewLine(visited.Expression.ToFullString()) ||
            !calls.Any(HasMultilineLambdaArgument))
        {
            return visited;
        }

        string statementIndent = GetLineIndent(visited.GetFirstToken());
        string indentUnit = GetBodyIndentUnit(visited, statementIndent);
        string chainIndent = statementIndent + indentUnit;
        string lambdaIndent = chainIndent + indentUnit;
        Dictionary<int, SyntaxTriviaList> leadingTriviaByToken = [];
        Dictionary<int, SyntaxTriviaList> trailingTriviaByToken = [];

        foreach (InvocationExpressionSyntax call in calls)
        {
            if (call.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                SetLeadingIndent(memberAccess.OperatorToken, chainIndent, leadingTriviaByToken, trailingTriviaByToken);
            }

            foreach (ArgumentSyntax argument in call.ArgumentList.Arguments)
            {
                if (argument.Expression is not LambdaExpressionSyntax { Body: ExpressionSyntax body } ||
                    !ContainsNewLine(body.ToFullString()))
                {
                    continue;
                }

                foreach (ExpressionSyntax operand in GetLogicalOrOperands(body))
                {
                    SetLeadingIndent(operand.GetFirstToken(), lambdaIndent, leadingTriviaByToken, trailingTriviaByToken);
                }

                SetLeadingIndent(call.ArgumentList.CloseParenToken, chainIndent, leadingTriviaByToken, trailingTriviaByToken);
            }
        }

        SetLeadingIndent(visited.CloseParenToken, statementIndent, leadingTriviaByToken, trailingTriviaByToken);
        return visited.ReplaceTokens(
            visited.DescendantTokens().Where(token =>
                leadingTriviaByToken.ContainsKey(token.SpanStart) ||
                trailingTriviaByToken.ContainsKey(token.SpanStart)),
            (original, rewritten) =>
            {
                if (leadingTriviaByToken.TryGetValue(original.SpanStart, out SyntaxTriviaList leadingTrivia))
                {
                    rewritten = rewritten.WithLeadingTrivia(leadingTrivia);
                }

                return trailingTriviaByToken.TryGetValue(original.SpanStart, out SyntaxTriviaList trailingTrivia)
                    ? rewritten.WithTrailingTrivia(trailingTrivia)
                    : rewritten;
            });
    }

    private static List<InvocationExpressionSyntax> GetReceiverChain(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return [];
        }

        List<InvocationExpressionSyntax> calls = GetReceiverChain(memberAccess.Expression);
        calls.Add(invocation);
        return calls;
    }

    private static bool HasMultilineLambdaArgument(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Any(argument =>
            argument.Expression is LambdaExpressionSyntax lambda &&
            ContainsNewLine(lambda.Body.ToFullString()));

    private static IEnumerable<ExpressionSyntax> GetLogicalOrOperands(ExpressionSyntax expression)
    {
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.LogicalOrExpression))
        {
            return GetLogicalOrOperands(binary.Left).Concat(GetLogicalOrOperands(binary.Right));
        }

        return [expression];
    }

    private static string GetLineIndent(SyntaxToken token)
    {
        string leadingText = string.Concat(token.LeadingTrivia.Select(trivia => trivia.ToFullString()));
        int lineBreak = Math.Max(leadingText.LastIndexOf('\n'), leadingText.LastIndexOf('\r'));
        string indent = leadingText[(lineBreak + 1)..];
        return indent.All(character => character is ' ' or '\t') ? indent : string.Empty;
    }

    private static string GetBodyIndentUnit(ForEachStatementSyntax statement, string statementIndent)
    {
        if (statement.Statement is not BlockSyntax block || block.Statements.Count == 0)
        {
            return "    ";
        }

        string bodyIndent = GetLineIndent(block.Statements[0].GetFirstToken());
        return bodyIndent.StartsWith(statementIndent, StringComparison.Ordinal) &&
               bodyIndent.Length > statementIndent.Length
            ? bodyIndent[statementIndent.Length..]
            : "    ";
    }

    private static void SetLeadingIndent(
        SyntaxToken token,
        string indent,
        IDictionary<int, SyntaxTriviaList> leadingTriviaByToken,
        IDictionary<int, SyntaxTriviaList> trailingTriviaByToken)
    {
        SyntaxToken previousToken = token.GetPreviousToken();
        if (CanReplaceWhitespace(token.LeadingTrivia) && CanReplaceWhitespace(previousToken.TrailingTrivia))
        {
            trailingTriviaByToken[previousToken.SpanStart] = default;
            leadingTriviaByToken[token.SpanStart] = SyntaxFactory.TriviaList(
                SyntaxFactory.EndOfLine(NewLine),
                SyntaxFactory.Whitespace(indent));
        }
    }

    private static bool CanReplaceWhitespace(SyntaxTriviaList triviaList) =>
        triviaList.All(trivia =>
            trivia.IsKind(SyntaxKind.WhitespaceTrivia) ||
            trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    private static bool ContainsNewLine(string text) =>
        text.Contains('\n') || text.Contains('\r');
}
