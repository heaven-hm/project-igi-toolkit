using System;
using System.Collections.Generic;
using System.Text;

namespace IGIEditor.Core
{
    /// <summary>
    /// A small boolean expression AST for QScript VarString trigger conditions.
    /// Unlike a flat "split on && / ||" approach this keeps operator precedence
    /// and parentheses, so loading a mission and saving it cannot silently turn
    /// "A && (B || C)" into "A || B || C".
    /// </summary>
    public abstract class TriggerExpression
    {
        public abstract string Render();

        public override string ToString() { return Render(); }

        public static TriggerExpression Leaf(string expression)
        {
            return new LeafNode(expression);
        }

        public static TriggerExpression Not(TriggerExpression inner)
        {
            return new NotNode(inner);
        }

        public static TriggerExpression And(TriggerExpression left, TriggerExpression right)
        {
            return new BinaryNode("&&", left, right);
        }

        public static TriggerExpression Or(TriggerExpression left, TriggerExpression right)
        {
            return new BinaryNode("||", left, right);
        }

        /// <summary>
        /// Combines a list of leaves with the requested operator. Returns null
        /// for an empty list.
        /// </summary>
        public static TriggerExpression Combine(IEnumerable<TriggerExpression> items, string op)
        {
            TriggerExpression result = null;
            foreach (var item in items)
            {
                if (item == null) continue;
                result = result == null ? item : new BinaryNode(op, result, item);
            }
            return result;
        }

        public List<string> Leaves()
        {
            var result = new List<string>();
            CollectLeaves(this, result);
            return result;
        }

        private static void CollectLeaves(TriggerExpression node, List<string> result)
        {
            var leaf = node as LeafNode;
            if (leaf != null) { result.Add(leaf.Value); return; }
            var not = node as NotNode;
            if (not != null) { CollectLeaves(not.Inner, result); return; }
            var binary = node as BinaryNode;
            if (binary != null)
            {
                CollectLeaves(binary.Left, result);
                CollectLeaves(binary.Right, result);
            }
        }

        /// <summary>Parses a QScript boolean condition. '\n' is treated as whitespace.</summary>
        public static TriggerExpression Parse(string text)
        {
            var parser = new ExpressionParser(text);
            var expression = parser.ParseOr();
            if (!parser.AtEnd)
                throw new FormatException("Unexpected token at position " + parser.Position + ".");
            return expression;
        }

        internal static bool IsValidLeaf(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.IndexOf('"') >= 0 || text.IndexOf(';') >= 0) return false;
            return true;
        }

        private sealed class LeafNode : TriggerExpression
        {
            internal readonly string Value;
            internal LeafNode(string value)
            {
                if (!IsValidLeaf(value))
                    throw new ArgumentException("Invalid trigger expression: " + value);
                Value = value.Trim();
            }
            public override string Render() { return Value; }
        }

        private sealed class NotNode : TriggerExpression
        {
            internal readonly TriggerExpression Inner;
            internal NotNode(TriggerExpression inner)
            {
                if (inner == null) throw new ArgumentNullException("inner");
                Inner = inner;
            }
            public override string Render()
            {
                var leaf = Inner as LeafNode;
                return leaf != null ? "!" + leaf.Value : "!(" + Inner.Render() + ")";
            }
        }

        private sealed class BinaryNode : TriggerExpression
        {
            internal readonly string Operator;
            internal readonly TriggerExpression Left;
            internal readonly TriggerExpression Right;
            internal BinaryNode(string op, TriggerExpression left, TriggerExpression right)
            {
                if (left == null || right == null) throw new ArgumentNullException("operand");
                Operator = op;
                Left = left;
                Right = right;
            }
            public override string Render()
            {
                return RenderChild(Left, false) + " " + Operator + " " + RenderChild(Right, true);
            }

            private string RenderChild(TriggerExpression child, bool isRight)
            {
                var binary = child as BinaryNode;
                if (binary == null) return child.Render();

                // Parentheses are required to preserve grouping when the child
                // binds looser than the parent, or is the right operand of the
                // same (left-associative) operator.
                bool sameOperator = string.Equals(binary.Operator, Operator, StringComparison.Ordinal);
                bool childBindsLooser = binary.Operator == "||" && Operator == "&&";
                bool needsParens = childBindsLooser || (isRight && sameOperator);
                return needsParens ? "(" + binary.Render() + ")" : binary.Render();
            }
        }

        private sealed class ExpressionParser
        {
            private readonly List<string> _tokens;
            private int _index;

            internal ExpressionParser(string text)
            {
                _tokens = Tokenize(text);
            }

            internal bool AtEnd { get { return _index >= _tokens.Count; } }
            internal int Position { get { return _index; } }

            internal TriggerExpression ParseOr()
            {
                var left = ParseAnd();
                while (Peek("||"))
                {
                    _index++;
                    left = Or(left, ParseAnd());
                }
                return left;
            }

            private TriggerExpression ParseAnd()
            {
                var left = ParseUnary();
                while (Peek("&&"))
                {
                    _index++;
                    left = And(left, ParseUnary());
                }
                return left;
            }

            private TriggerExpression ParseUnary()
            {
                if (Peek("!"))
                {
                    _index++;
                    return Not(ParseUnary());
                }
                return ParsePrimary();
            }

            private TriggerExpression ParsePrimary()
            {
                if (AtEnd) throw new FormatException("Unexpected end of trigger expression.");
                var token = _tokens[_index];
                if (token == "(")
                {
                    _index++;
                    var inner = ParseOr();
                    if (!Peek(")")) throw new FormatException("Missing ')' in trigger expression.");
                    _index++;
                    return inner;
                }
                if (token == ")" || token == "&&" || token == "||" || token == "!")
                    throw new FormatException("Unexpected token '" + token + "' in trigger expression.");
                _index++;
                return Leaf(token);
            }

            private bool Peek(string token)
            {
                return !AtEnd && string.Equals(_tokens[_index], token, StringComparison.Ordinal);
            }

            private static List<string> Tokenize(string text)
            {
                var tokens = new List<string>();
                if (string.IsNullOrEmpty(text)) return tokens;

                // QSC uses the literal two-character sequence \n for a newline
                // inside VarStrings. Normalise it to whitespace before parsing.
                var normalized = text.Replace("\\n", " ");
                var buffer = new StringBuilder();
                for (int i = 0; i < normalized.Length; i++)
                {
                    char c = normalized[i];
                    if (char.IsWhiteSpace(c))
                    {
                        Flush(buffer, tokens);
                        continue;
                    }
                    if (c == '&' || c == '|')
                    {
                        Flush(buffer, tokens);
                        bool doubled = i + 1 < normalized.Length && normalized[i + 1] == c;
                        if (!doubled)
                            throw new FormatException("Single '" + c + "' is not a valid QScript operator.");
                        tokens.Add(c == '&' ? "&&" : "||");
                        i++;
                        continue;
                    }
                    if (c == '(' || c == ')' || c == '!')
                    {
                        Flush(buffer, tokens);
                        tokens.Add(c.ToString());
                        continue;
                    }
                    buffer.Append(c);
                }
                Flush(buffer, tokens);
                return tokens;
            }

            private static void Flush(StringBuilder buffer, List<string> tokens)
            {
                if (buffer.Length == 0) return;
                tokens.Add(buffer.ToString());
                buffer.Length = 0;
            }
        }
    }
}
