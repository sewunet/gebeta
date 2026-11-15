using System.Globalization;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Formulas;

public sealed class FormulaException(string message) : DomainException("payroll.formula", message);

/// <summary>Values and functions a formula may use while it is evaluated.</summary>
public interface IFormulaScope
{
    bool TryGetVariable(string name, out decimal value);

    /// <summary>Extra functions beyond the built-in ones, e.g. <c>income_tax</c>. Return false when unknown.</summary>
    bool TryCall(string name, IReadOnlyList<decimal> arguments, out decimal result);
}

/// <summary>
/// A salary formula, compiled once. The language is the Python subset ERPNext salary structures use,
/// so existing formulas run unchanged, but evaluation is safe and exact:
/// <list type="bullet">
/// <item>numbers are <see cref="decimal"/> (never floating point);</item>
/// <item>only variables of the scope and these functions exist: <c>if(c, a, b)</c>, <c>min</c>, <c>max</c>,
/// <c>round(x[, n])</c>, <c>flt(x[, n])</c>, <c>floor</c>, <c>ceil</c>, <c>abs</c>, <c>int</c>, plus scope
/// functions such as <c>income_tax(x)</c>;</item>
/// <item>operators: <c>+ - * / // % **</c>, comparisons (chainable), <c>and or not</c>, <c>a if c else b</c>;</item>
/// <item>no attribute access, imports, loops or other code; length and nesting are limited.</item>
/// </list>
/// Booleans are 1 and 0. Rounding is half away from zero, as everywhere in Gebeta.
/// </summary>
public sealed class Formula
{
    public const int MaxLength = 2000;
    private const int MaxDepth = 60;

    private static readonly HashSet<string> BuiltIns = ["if", "min", "max", "round", "flt", "floor", "ceil", "abs", "int"];

    private readonly Node _root;

    private Formula(string text, Node root, IReadOnlySet<string> variables, IReadOnlySet<string> functions)
    {
        Text = text;
        _root = root;
        Variables = variables;
        Functions = functions;
    }

    public string Text { get; }

    /// <summary>Every variable the formula reads (used for dependency order and validation).</summary>
    public IReadOnlySet<string> Variables { get; }

    /// <summary>Non built-in functions the formula calls.</summary>
    public IReadOnlySet<string> Functions { get; }

    public static Result<Formula> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error.Validation("A formula is required.");
        }

        if (text.Length > MaxLength)
        {
            return Error.Validation($"A formula may have at most {MaxLength} characters.");
        }

        try
        {
            var parser = new Parser(Tokenizer.Tokenize(text));
            var root = parser.ParseAll();
            var variables = new HashSet<string>(StringComparer.Ordinal);
            var functions = new HashSet<string>(StringComparer.Ordinal);
            root.Collect(variables, functions);
            return new Formula(text.Trim(), root, variables, functions);
        }
        catch (FormulaException ex)
        {
            return Error.Validation($"Formula '{text.Trim()}': {ex.Message}");
        }
    }

    public decimal Evaluate(IFormulaScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            return _root.Evaluate(scope);
        }
        catch (DivideByZeroException)
        {
            throw new FormulaException($"Division by zero in '{Text}'.");
        }
        catch (OverflowException)
        {
            throw new FormulaException($"The result of '{Text}' is too large.");
        }
    }

    public override string ToString() => Text;

    // ---------------------------------------------------------------- tokens

    private enum TokenKind
    {
        Number,
        Identifier,
        Operator,
        LeftParen,
        RightParen,
        Comma,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static class Tokenizer
    {
        private static readonly string[] Operators = ["**", "//", "<=", ">=", "==", "!=", "+", "-", "*", "/", "%", "<", ">"];

        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                {
                    var start = i;
                    while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.' || text[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Number, text[start..i].Replace("_", string.Empty, StringComparison.Ordinal), start));
                    continue;
                }

                if (char.IsAsciiLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
                    continue;
                }

                switch (c)
                {
                    case '(':
                        tokens.Add(new Token(TokenKind.LeftParen, "(", i++));
                        continue;
                    case ')':
                        tokens.Add(new Token(TokenKind.RightParen, ")", i++));
                        continue;
                    case ',':
                        tokens.Add(new Token(TokenKind.Comma, ",", i++));
                        continue;
                }

                var op = Operators.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0);
                if (op is null)
                {
                    throw new FormulaException($"unexpected character '{c}' at position {i + 1}.");
                }

                tokens.Add(new Token(TokenKind.Operator, op, i));
                i += op.Length;
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
            return tokens;
        }
    }

    // ---------------------------------------------------------------- parser (Python precedence)

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;
        private int _depth;

        private Token Current => tokens[_position];

        public Node ParseAll()
        {
            var node = ParseTernary();
            if (Current.Kind != TokenKind.End)
            {
                throw new FormulaException($"unexpected '{Current.Text}' at position {Current.Position + 1}.");
            }

            return node;
        }

        private bool IsKeyword(string keyword) => Current.Kind == TokenKind.Identifier && Current.Text == keyword;

        private bool IsOperator(string op) => Current.Kind == TokenKind.Operator && Current.Text == op;

        private Token Advance() => tokens[_position++];

        private Node Enter(Func<Node> parse)
        {
            if (++_depth > MaxDepth)
            {
                throw new FormulaException("the formula is nested too deeply.");
            }

            try
            {
                return parse();
            }
            finally
            {
                _depth--;
            }
        }

        // a if c else b  (right associative)
        private Node ParseTernary() => Enter(() =>
        {
            var whenTrue = ParseOr();
            if (!IsKeyword("if"))
            {
                return whenTrue;
            }

            Advance();
            var condition = ParseOr();
            if (!IsKeyword("else"))
            {
                throw new FormulaException("'if' without 'else'.");
            }

            Advance();
            var whenFalse = ParseTernary();
            return new TernaryNode(condition, whenTrue, whenFalse);
        });

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (IsKeyword("or"))
            {
                Advance();
                left = new LogicalNode(left, ParseAnd(), isAnd: false);
            }

            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseNot();
            while (IsKeyword("and"))
            {
                Advance();
                left = new LogicalNode(left, ParseNot(), isAnd: true);
            }

            return left;
        }

        private Node ParseNot()
        {
            if (IsKeyword("not"))
            {
                Advance();
                return Enter(() => new NotNode(ParseNot()));
            }

            return ParseComparison();
        }

        private Node ParseComparison()
        {
            var first = ParseAdditive();
            var operands = new List<Node> { first };
            var operators = new List<string>();
            while (Current.Kind == TokenKind.Operator && Current.Text is "<" or "<=" or ">" or ">=" or "==" or "!=")
            {
                operators.Add(Advance().Text);
                operands.Add(ParseAdditive());
            }

            return operators.Count == 0 ? first : new ComparisonNode(operands, operators);
        }

        private Node ParseAdditive()
        {
            var left = ParseTerm();
            while (IsOperator("+") || IsOperator("-"))
            {
                var op = Advance().Text;
                left = new BinaryNode(op, left, ParseTerm());
            }

            return left;
        }

        private Node ParseTerm()
        {
            var left = ParseUnary();
            while (IsOperator("*") || IsOperator("/") || IsOperator("//") || IsOperator("%"))
            {
                var op = Advance().Text;
                left = new BinaryNode(op, left, ParseUnary());
            }

            return left;
        }

        private Node ParseUnary()
        {
            if (IsOperator("-") || IsOperator("+"))
            {
                var op = Advance().Text;
                return Enter(() => op == "-" ? new NegateNode(ParseUnary()) : ParseUnary());
            }

            return ParsePower();
        }

        private Node ParsePower()
        {
            var baseNode = ParsePrimary();
            if (IsOperator("**"))
            {
                Advance();
                return new BinaryNode("**", baseNode, Enter(ParseUnary));
            }

            return baseNode;
        }

        private Node ParsePrimary()
        {
            var token = Advance();
            switch (token.Kind)
            {
                case TokenKind.Number:
                    if (!decimal.TryParse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    {
                        throw new FormulaException($"'{token.Text}' is not a number.");
                    }

                    return new NumberNode(number);

                case TokenKind.LeftParen:
                    var inner = ParseTernary();
                    Expect(TokenKind.RightParen, ")");
                    return inner;

                case TokenKind.Identifier:
                    if (token.Text is "True" or "true")
                    {
                        return new NumberNode(1m);
                    }

                    if (token.Text is "False" or "false" or "None")
                    {
                        return new NumberNode(0m);
                    }

                    // "if(" is the if() function; a bare "if" is the keyword of "a if c else b".
                    var isIfFunction = token.Text == "if" && Current.Kind == TokenKind.LeftParen;
                    if (!isIfFunction && token.Text is "and" or "or" or "not" or "if" or "else")
                    {
                        throw new FormulaException($"unexpected '{token.Text}' at position {token.Position + 1}.");
                    }

                    if (Current.Kind == TokenKind.LeftParen)
                    {
                        Advance();
                        var arguments = new List<Node>();
                        if (Current.Kind != TokenKind.RightParen)
                        {
                            do
                            {
                                arguments.Add(ParseTernary());
                            }
                            while (Current.Kind == TokenKind.Comma && Advance().Kind == TokenKind.Comma);
                        }

                        Expect(TokenKind.RightParen, ")");
                        return new CallNode(token.Text, arguments);
                    }

                    return new VariableNode(token.Text);

                default:
                    throw new FormulaException(token.Kind == TokenKind.End
                        ? "the formula ends too early."
                        : $"unexpected '{token.Text}' at position {token.Position + 1}.");
            }
        }

        private void Expect(TokenKind kind, string text)
        {
            if (Current.Kind != kind)
            {
                throw new FormulaException($"expected '{text}' at position {Current.Position + 1}.");
            }

            Advance();
        }
    }

    // ---------------------------------------------------------------- nodes

    private abstract class Node
    {
        public abstract decimal Evaluate(IFormulaScope scope);

        public virtual void Collect(HashSet<string> variables, HashSet<string> functions)
        {
        }
    }

    private sealed class NumberNode(decimal value) : Node
    {
        public override decimal Evaluate(IFormulaScope scope) => value;
    }

    private sealed class VariableNode(string name) : Node
    {
        public override decimal Evaluate(IFormulaScope scope) =>
            scope.TryGetVariable(name, out var value) ? value : throw new FormulaException($"unknown name '{name}'.");

        public override void Collect(HashSet<string> variables, HashSet<string> functions) => variables.Add(name);
    }

    private sealed class NegateNode(Node operand) : Node
    {
        public override decimal Evaluate(IFormulaScope scope) => -operand.Evaluate(scope);

        public override void Collect(HashSet<string> variables, HashSet<string> functions) => operand.Collect(variables, functions);
    }

    private sealed class NotNode(Node operand) : Node
    {
        public override decimal Evaluate(IFormulaScope scope) => operand.Evaluate(scope) == 0m ? 1m : 0m;

        public override void Collect(HashSet<string> variables, HashSet<string> functions) => operand.Collect(variables, functions);
    }

    private sealed class BinaryNode(string op, Node left, Node right) : Node
    {
        public override decimal Evaluate(IFormulaScope scope)
        {
            var a = left.Evaluate(scope);
            var b = right.Evaluate(scope);
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => a / b,
                "//" => Math.Floor(a / b),
                "%" => a - (b * Math.Floor(a / b)), // Python modulo: sign follows the divisor
                "**" => Power(a, b),
                _ => throw new FormulaException($"unknown operator '{op}'."),
            };
        }

        public override void Collect(HashSet<string> variables, HashSet<string> functions)
        {
            left.Collect(variables, functions);
            right.Collect(variables, functions);
        }

        private static decimal Power(decimal value, decimal exponent)
        {
            if (exponent != Math.Floor(exponent) || exponent is < 0 or > 32)
            {
                throw new FormulaException("** supports whole exponents from 0 to 32 only.");
            }

            var result = 1m;
            for (var i = 0; i < (int)exponent; i++)
            {
                result *= value;
            }

            return result;
        }
    }

    private sealed class ComparisonNode(List<Node> operands, List<string> operators) : Node
    {
        public override decimal Evaluate(IFormulaScope scope)
        {
            var left = operands[0].Evaluate(scope);
            for (var i = 0; i < operators.Count; i++)
            {
                var right = operands[i + 1].Evaluate(scope);
                var holds = operators[i] switch
                {
                    "<" => left < right,
                    "<=" => left <= right,
                    ">" => left > right,
                    ">=" => left >= right,
                    "==" => left == right,
                    _ => left != right,
                };
                if (!holds)
                {
                    return 0m;
                }

                left = right;
            }

            return 1m;
        }

        public override void Collect(HashSet<string> variables, HashSet<string> functions) =>
            operands.ForEach(o => o.Collect(variables, functions));
    }

    private sealed class LogicalNode(Node left, Node right, bool isAnd) : Node
    {
        public override decimal Evaluate(IFormulaScope scope)
        {
            var a = left.Evaluate(scope) != 0m;
            if (isAnd ? !a : a)
            {
                return a ? 1m : 0m;
            }

            return right.Evaluate(scope) != 0m ? 1m : 0m;
        }

        public override void Collect(HashSet<string> variables, HashSet<string> functions)
        {
            left.Collect(variables, functions);
            right.Collect(variables, functions);
        }
    }

    private sealed class TernaryNode(Node condition, Node whenTrue, Node whenFalse) : Node
    {
        public override decimal Evaluate(IFormulaScope scope) =>
            condition.Evaluate(scope) != 0m ? whenTrue.Evaluate(scope) : whenFalse.Evaluate(scope);

        public override void Collect(HashSet<string> variables, HashSet<string> functions)
        {
            condition.Collect(variables, functions);
            whenTrue.Collect(variables, functions);
            whenFalse.Collect(variables, functions);
        }
    }

    private sealed class CallNode(string name, List<Node> arguments) : Node
    {
        public override decimal Evaluate(IFormulaScope scope)
        {
            if (name == "if")
            {
                RequireCount(3, 3);
                return arguments[0].Evaluate(scope) != 0m ? arguments[1].Evaluate(scope) : arguments[2].Evaluate(scope);
            }

            var values = arguments.Select(a => a.Evaluate(scope)).ToList();
            switch (name)
            {
                case "min":
                    RequireCount(1, int.MaxValue);
                    return values.Min();
                case "max":
                    RequireCount(1, int.MaxValue);
                    return values.Max();
                case "round":
                case "flt":
                    RequireCount(1, 2);
                    var digits = values.Count == 2 ? (int)values[1] : name == "flt" ? 9 : 0;
                    return Math.Round(values[0], Math.Clamp(digits, 0, 9), MidpointRounding.AwayFromZero);
                case "floor":
                    RequireCount(1, 1);
                    return Math.Floor(values[0]);
                case "ceil":
                    RequireCount(1, 1);
                    return Math.Ceiling(values[0]);
                case "abs":
                    RequireCount(1, 1);
                    return Math.Abs(values[0]);
                case "int":
                    RequireCount(1, 1);
                    return Math.Truncate(values[0]);
            }

            return scope.TryCall(name, values, out var result) ? result : throw new FormulaException($"unknown function '{name}'.");
        }

        public override void Collect(HashSet<string> variables, HashSet<string> functions)
        {
            if (!BuiltIns.Contains(name))
            {
                functions.Add(name);
            }

            arguments.ForEach(a => a.Collect(variables, functions));
        }

        private void RequireCount(int min, int max)
        {
            if (arguments.Count < min || arguments.Count > max)
            {
                var expected = min == max ? $"{min}" : max == int.MaxValue ? $"{min} or more" : $"{min} to {max}";
                throw new FormulaException($"{name}() takes {expected} argument(s).");
            }
        }
    }
}
