using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Direct predicates over original syntax/trivia. This class has no source-writing API.
internal sealed class StyleVerifier
{
    private readonly Dictionary<string, string> settings = new(StringComparer.Ordinal);

    public StyleVerifier(string editorConfig)
    {
        bool csharp = false;
        foreach (string line in File.ReadLines(editorConfig))
        {
            string text = line.Trim();
            if (text.StartsWith('['))
            {
                csharp = text == "[*.cs]";
                continue;
            }

            if (!csharp || text.StartsWith('#')) { continue; }
            int equals = text.IndexOf('=');
            if (equals >= 0) { settings[text[..equals].Trim()] = text[(equals + 1)..].Trim(); }
        }
    }

    public bool Check(SyntaxNode root, string source, string path)
    {
        bool clean = true;
        void Report(SyntaxNode node, string rule)
        {
            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Console.Error.WriteLine($"{path}({line}): {rule}");
            clean = false;
        }

        bool Enabled(string key, string value) => settings.GetValueOrDefault("resharper_csharp_" + key) == value;
        bool Required(string construct) => Enabled("braces_for_" + construct, "required");
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            (StatementSyntax? body, string construct) = node switch
            {
                IfStatementSyntax n => (n.Statement, "ifelse"),
                ElseClauseSyntax n when n.Statement is not IfStatementSyntax => (n.Statement, "ifelse"),
                ForStatementSyntax n => (n.Statement, "for"),
                CommonForEachStatementSyntax n => (n.Statement, "foreach"),
                WhileStatementSyntax n => (n.Statement, "while"),
                DoStatementSyntax n => (n.Statement, "dowhile"),
                UsingStatementSyntax n => (n.Statement, "using"),
                LockStatementSyntax n => (n.Statement, "lock"),
                FixedStatementSyntax n => (n.Statement, "fixed"),
                _ => (null, "")
            };
            if (body is not null && body is not BlockSyntax && Required(construct))
            {
                Report(node, "braces required for " + construct);
            }

            if (node is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression) &&
                Enabled("space_after_logical_not_op", "true"))
            {
                string gap = source[unary.OperatorToken.Span.End..unary.Operand.SpanStart];
                if (gap.Length == 0) { Report(node, "space required after logical NOT"); }
            }

            if (node is ArgumentListSyntax arguments && arguments.Arguments.Count > 0 &&
                Enabled("wrap_before_invocation_rpar", "true"))
            {
                CheckCloser(arguments.OpenParenToken, arguments.CloseParenToken, arguments.Arguments[^1], node);
                if (Enabled("wrap_arguments_style", "chop_if_long"))
                {
                    CheckItems(arguments.Arguments.Cast<SyntaxNode>().ToArray(), node);
                }
            }

            if (node is ParameterListSyntax parameters && parameters.Parameters.Count > 0 &&
                Enabled("wrap_before_declaration_rpar", "true"))
            {
                CheckCloser(parameters.OpenParenToken, parameters.CloseParenToken, parameters.Parameters[^1], node);
                if (Enabled("wrap_parameters_style", "chop_if_long"))
                {
                    CheckItems(parameters.Parameters.Cast<SyntaxNode>().ToArray(), node);
                }
                if (Enabled("wrap_after_declaration_lpar", "true") &&
                    Line(parameters.CloseParenToken) > Line(parameters.OpenParenToken) &&
                    Line(parameters.Parameters[0].GetFirstToken()) == Line(parameters.OpenParenToken))
                {
                    Report(node, "wrapped declaration requires first parameter after opening line");
                }
            }

            if (node is MemberAccessExpressionSyntax member && Enabled("wrap_after_dot_in_method_calls", "false") &&
                member.Parent is InvocationExpressionSyntax && Line(member.Name.GetFirstToken()) > Line(member.OperatorToken))
            {
                Report(node, "wrapped method chain requires a leading dot");
            }

            if (node is TypeDeclarationSyntax type)
            {
                for (int i = 1; i < type.Members.Count; i++)
                {
                    MemberDeclarationSyntax previous = type.Members[i - 1];
                    MemberDeclarationSyntax current = type.Members[i];
                    if ((IsInvocable(previous) || IsInvocable(current)) &&
                        Enabled("blank_lines_around_invocable", "1") &&
                        Enabled("blank_lines_around_single_line_invocable", "1"))
                    {
                        CheckBlank(previous, current, "blank line around invocable member");
                    }
                }
            }

            if (node is BlockSyntax block)
            {
                for (int i = 1; i < block.Statements.Count; i++)
                {
                    StatementSyntax previous = block.Statements[i - 1];
                    StatementSyntax current = block.Statements[i];
                    if ((previous is LocalFunctionStatementSyntax || current is LocalFunctionStatementSyntax) &&
                        Enabled("blank_lines_around_local_method", "1") &&
                        Enabled("blank_lines_around_single_line_local_method", "1"))
                    {
                        CheckBlank(previous, current, "blank line around local method");
                    }

                    if (current is ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or
                        ContinueStatementSyntax or GotoStatementSyntax or YieldStatementSyntax &&
                        Enabled("blank_lines_before_control_transfer_statements", "1") &&
                        !(previous is YieldStatementSyntax && current is YieldStatementSyntax))
                    {
                        CheckBlank(previous, current, "blank line before control transfer");
                    }

                    if ((HasBlock(current) && Enabled("blank_lines_before_block_statements", "1")) ||
                        (HasBlock(previous) && Enabled("blank_lines_after_block_statements", "1")))
                    {
                        CheckBlank(previous, current, "blank line around block statement");
                    }
                }
            }
        }

        // Examine whitespace trivia only: literal contents, comments and disabled code are excluded.
        if (Enabled("keep_blank_lines_in_code", "1") && Enabled("keep_blank_lines_in_declarations", "1"))
        {
            SyntaxToken previous = default;
            foreach (SyntaxToken token in root.DescendantTokens())
            {
                int newlines = 0;
                foreach (SyntaxTrivia trivia in previous.TrailingTrivia.Concat(token.LeadingTrivia))
                {
                    if (trivia.IsKind(SyntaxKind.EndOfLineTrivia)) { newlines++; }
                    else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia)) { newlines = 0; }
                    if (newlines > 2)
                    {
                        Report(token.Parent!, "at most one consecutive blank line in syntax trivia");
                        break;
                    }
                }

                previous = token;
            }
        }

        return clean;

        void CheckCloser(SyntaxToken open, SyntaxToken close, SyntaxNode last, SyntaxNode node)
        {
            // Only wrapped lists: compact single-line calls/declarations remain compact.
            int openLine = open.GetLocation().GetLineSpan().StartLinePosition.Line;
            int lastLine = last.GetLastToken().GetLocation().GetLineSpan().EndLinePosition.Line;
            int closeLine = close.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (lastLine > openLine && closeLine == lastLine)
            {
                Report(node, "wrapped argument/parameter list requires closing parenthesis on a separate line");
            }
            else if (node is ParameterListSyntax && closeLine > lastLine && Indent(open) != Indent(close))
            {
                Report(node, "wrapped closing parenthesis must align with opening line indentation");
            }
        }

        void CheckItems(SyntaxNode[] items, SyntaxNode node)
        {
            int[] lines = items.Select(item => Line(item.GetFirstToken())).ToArray();
            if (lines.Distinct().Count() > 1 && lines.Distinct().Count() < items.Length)
            {
                Report(node, "wrapped arguments/parameters require one item per line");
            }
        }

        int Line(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line;

        string Indent(SyntaxToken token)
        {
            int start = source.LastIndexOf('\n', Math.Max(0, token.SpanStart - 1)) + 1;
            int end = start;
            while (end < source.Length && source[end] is ' ' or '\t') { end++; }
            return source[start..end];
        }

        void CheckBlank(SyntaxNode previous, SyntaxNode current, string rule)
        {
            string gap = source[previous.Span.End..current.SpanStart];
            if (!Regex.IsMatch(gap, @"\n[ \t]*\n")) { Report(current, rule); }
        }
    }

    private static bool IsInvocable(MemberDeclarationSyntax member) =>
        member is BaseMethodDeclarationSyntax;

    private static bool HasBlock(StatementSyntax statement) => statement is not BlockSyntax &&
        statement.ChildNodes().Any(child => child is BlockSyntax);
}
