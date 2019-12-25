using System;
using System.Collections.Generic;
using System.Linq;

namespace tirr {
    public abstract class Statement {
        public bool Expired = false;
        public abstract override string ToString();
    }

    public class SCall : Statement {
        public string Callee;
        public Variable Destination;
        public List<Variable> Arguments = new List<Variable>();

        public override string ToString() {
            return $"{Destination.Name} = {Callee}({string.Join(',', Arguments.Select(arg => arg.Name))});";
        }
    }

    public class SAssign : Statement {
        public Variable Destination;
        public Variable Source;

        public override string ToString() {
            return $"{Destination.Name} = {Source.Name};";
        }
    }

    public class SImm : Statement {
        public Variable Destination;
        public string Immediate;

        public override string ToString() {
            return $"{Destination.Name} = {Immediate};";
        }
    }

    public class SBinary : Statement {
        public Variable Destination, Operand1, Operand2;
        public string Operation;

        public override string ToString() {
            return $"{Destination.Name} = {Operand1.Name} {Operation} {Operand2.Name};";
        }
    }

    public class SCast : Statement {
        public Variable Destination, Source;

        public override string ToString() {
            return $"{Destination.Name} = ({Destination.Type}){Source.Name};";
        }
    }

    public class SAlloca : Statement {
        public Variable Destination;
        public string Size;

        public override string ToString() {
            return $"uint8_t {Destination.Name}[{Size}];";
        }
    }

    public class SDeref : Statement {
        public Variable Destination, Address;

        public override string ToString() {
            return $"{Destination.Name} = *({Destination.Type}*){Address.Name};";
        }
    }

    public class SModifyRef : Statement {
        public Variable Destination, Source;

        public override string ToString() {
            return $"*({Source.Type}*){Destination.Name} = {Source.Name};";
        }
    }
    
    public class Block {
        public List<Statement> Statements = new List<Statement>();
        public Variable Condition;
        public Block TrueNext;
        public Block FalseNext;
        public bool Expired = false;
        public string GetEntryTag() => $"bob_0x{GetHashCode():x8}"; // begin of block # 0x......
    }

    public class Variable {
        public bool IsConstant = false;
        public string Type;
        public string Name;
    }

    public class Function {
        public string Name;

        public Dictionary<string, Variable> Variables = new Dictionary<string, Variable>();

        public Variable GetOrDeclareVariable(string name, string type) {
            if (Variables.ContainsKey(name)) return Variables[name];
            var v = new Variable { Name = name, Type = type };
            return (Variables[name] = v);
        }

        public List<Variable> Arguments = new List<Variable>();
        public Variable ReturnValue;

        public List<Block> Blocks = new List<Block>();
        public Block Entry;

        public Block NewBlock() {
            var b = new Block();
            Blocks.Add(b);
            return b;
        }

        public void Output() {
            Console.WriteLine($"{ReturnValue.Type} {Name}({string.Join(',', Arguments.Select(arg => $"{arg.Type} {arg.Name}"))}) {{");

            foreach (var value in Variables.Values) {
                if (!Arguments.Contains(value) && value.Type != "<ignore>") Console.WriteLine($"{value.Type} {value.Name};");
            }

            Console.WriteLine($"goto {Entry.GetEntryTag()};");

            foreach (var block in Blocks) if (!block.Expired){
                Console.WriteLine($"{block.GetEntryTag()}:");

                foreach (var blockStatement in block.Statements) if (!blockStatement.Expired){
                    Console.WriteLine(blockStatement.ToString());
                }

                if (block.Condition == null) {
                    if (block.TrueNext == null) Console.WriteLine($"return {ReturnValue.Name};");
                    else Console.WriteLine($"goto {block.TrueNext.GetEntryTag()};");
                } else {
                    Console.WriteLine($"if ({block.Condition.Name}) {{ goto {block.TrueNext.GetEntryTag()}; }} else {{ goto {block.FalseNext.GetEntryTag()}; }}");
                }
            }

            Console.WriteLine($"}}");
        }
    }
}
