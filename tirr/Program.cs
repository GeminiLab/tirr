using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;

namespace tirr {
    public static class Program {
        public static string TypeToType(string v) {
            return v switch {
                "IInt8" => "int8_t",
                "IInt16" => "int16_t",
                "IInt32" => "int32_t",
                "IInt64" => "int64_t",
                "IUInt8" => "uint8_t",
                "IUInt16" => "uint16_t",
                "IUInt32" => "uint32_t",
                "IUInt64" => "uint64_t",
                _ => v,
            };
        }

        public static SCall ProcessInvoke(JsonElement elem, Function function) {
            var invoked = elem.GetProperty("iInvokedFun").GetString();
            var rtype = TypeToType(elem.GetProperty("iUName")[0].GetString());
            var rname = elem.GetProperty("iUName")[1].GetString();
            var args = elem.GetProperty("iArguments");
            var argc = args.GetArrayLength();

            var argList = new List<string>();
            for (int i = 0; i < argc; ++i) {
                argList.Add(args[i].GetString());
            }

            return new SCall {
                Arguments = argList.Select(name => function.GetOrDeclareVariable(name, "<err>")).ToList(),
                Callee = invoked,
                Destination = function.GetOrDeclareVariable(rname, rtype)
            };
        }

        public static SImm ProcessImm(JsonElement elem, Function function) {
            var rtype = TypeToType(elem.GetProperty("iUName")[0].GetString());
            var rname = elem.GetProperty("iUName")[1].GetString();
            var lit = elem.GetProperty("iLiteral").GetString();

            return new SImm {
                Destination = function.GetOrDeclareVariable(rname, rtype),
                Immediate = lit
            };
        }
        
        public static SBinary ProcessBinary(JsonElement elem, Function function) {
            var rtype = TypeToType(elem.GetProperty("iUName")[0].GetString());
            var rname = elem.GetProperty("iUName")[1].GetString();
            var op = elem.GetProperty("iOperator").GetString();
            var op1 = elem.GetProperty("iOperand1").GetString();
            var op2 = elem.GetProperty("iOperand2").GetString();

            return new SBinary {
                Destination = function.GetOrDeclareVariable(rname, rtype),
                Operand1 = function.GetOrDeclareVariable(op1, "<err>"),
                Operand2 = function.GetOrDeclareVariable(op2, "<err>"),
                Operation = op
            };
        }

        public static SCast ProcessCast(JsonElement elem, Function function) {
            var rtype = TypeToType(elem.GetProperty("iUName")[0].GetString());
            var rname = elem.GetProperty("iUName")[1].GetString();
            var op = elem.GetProperty("iOperand").GetString();

            return new SCast {
                Destination = function.GetOrDeclareVariable(rname, rtype),
                Source = function.GetOrDeclareVariable(op, "<err>")
            };
        }

        public static SAlloca ProcessAlloca(JsonElement elem, Function function) {
            var name = elem.GetProperty("iName").GetString();
            var size = elem.GetProperty("iMemSize").ToString();

            return new SAlloca {
                Destination = function.GetOrDeclareVariable(name, "<ignore>"),
                Size = size
            };
        }

        /*
        public static void ProcessFree(JsonElement elem) {
            var name = elem.GetProperty("iName").GetString();

            Console.WriteLine($"free({name});");
        }
        */

        public static SDeref ProcessDeref(JsonElement elem, Function function) {
            var source = elem.GetProperty("iSource").GetString();
            var dtype = TypeToType(elem.GetProperty("iDestR")[0].ToString());
            var dname = TypeToType(elem.GetProperty("iDestR")[1].ToString());

            return new SDeref {
                Address = function.GetOrDeclareVariable(source, "<err>"),
                Destination = function.GetOrDeclareVariable(dname, dtype)
            };
        }

        public static SModifyRef ProcessModifyRef(JsonElement elem, Function function) {
            var dest = elem.GetProperty("iDest").GetString();
            var stype = TypeToType(elem.GetProperty("iSourceR")[0].ToString());
            var sname = TypeToType(elem.GetProperty("iSourceR")[1].ToString());

            return new SModifyRef {
                Destination = function.GetOrDeclareVariable(dest, "<err>"),
                Source = function.GetOrDeclareVariable(sname, stype)
            };
        }

        public static void ProcessTernary(JsonElement elem, Function function, ref Block block) {
            var rvs = elem.GetProperty("iLName");
            var rvList = new List<(string Type, string Name)>();

            var rvc = rvs.GetArrayLength();
            for (int i = 0; i < rvc; ++i) {
                rvList.Add((TypeToType(rvs[i][0].GetString()), rvs[i][1].GetString()));
            }

            var op1 = elem.GetProperty("iTOperand1").GetString();
            var op2seq = elem.GetProperty("iTOperand2")[0];
            var op2 = elem.GetProperty("iTOperand2")[1];
            var op3seq = elem.GetProperty("iTOperand3")[0];
            var op3 = elem.GetProperty("iTOperand3")[1];

            var op2List = new List<string>();
            var op3List = new List<string>();

            for (int i = 0; i < rvc; ++i) {
                op2List.Add(op2[i].GetString());
                op3List.Add(op3[i].GetString());
            }
            
            block.Condition = function.GetOrDeclareVariable(op1, "<err>");
            var blk2 = function.NewBlock();
            var blk3 = function.NewBlock();
            block.TrueNext = blk2;
            block.FalseNext = blk3;

            var op2seqc = op2seq.GetArrayLength();
            for (int i = 0; i < op2seqc; ++i) {
                ProcessStatement(op2seq[i], function, ref blk2);
            }
            for (int i = 0; i < rvc; ++i) {
                blk2.Statements.Add(new SAssign { Destination = function.GetOrDeclareVariable(rvList[i].Name, rvList[i].Type), Source = function.GetOrDeclareVariable(op2List[i], "<err>") });
            }

            var op3seqc = op3seq.GetArrayLength();
            for (int i = 0; i < op3seqc; ++i) {
                ProcessStatement(op3seq[i], function, ref blk3);
            }
            for (int i = 0; i < rvc; ++i) {
                blk3.Statements.Add(new SAssign { Destination = function.GetOrDeclareVariable(rvList[i].Name, rvList[i].Type), Source = function.GetOrDeclareVariable(op3List[i], "<err>") });
            }

            var newBlk = function.NewBlock();
            blk2.Condition = blk3.Condition = null;
            blk2.TrueNext = blk2.FalseNext = blk3.TrueNext = blk3.FalseNext = newBlk;
            block = newBlk;
        }

        public static void ProcessStatement(JsonElement elem, Function function, ref Block block) {
            var tag = elem.GetProperty("tag").GetString();

            switch (tag) {
            case "IRInvoke":
                block.Statements.Add(ProcessInvoke(elem, function));
                break;
            case "IRImm":
                block.Statements.Add(ProcessImm(elem, function));
                break;
            case "IRBinary":
                block.Statements.Add(ProcessBinary(elem, function));
                break;
            case "IRCast":
                block.Statements.Add(ProcessCast(elem, function));
                break;
            case "IRAlloca":
                block.Statements.Add(ProcessAlloca(elem, function));
                break;
            /*
            case "IRFree":
                ProcessFree(elem);
                break;
                */
            case "IRDeref":
                block.Statements.Add(ProcessDeref(elem, function));
                break;
            case "IRModifyRef":
                block.Statements.Add(ProcessModifyRef(elem, function));
                break;
            case "IRTernary":
                ProcessTernary(elem, function, ref block);
                break;
            }
        }

        public static void ProcessFunction(JsonElement elem) {
            var name = elem.GetProperty("iName").GetString();
            var args = elem.GetProperty("iArgumentField");
            var rtype = TypeToType(elem.GetProperty("iReturnField")[0].GetString());
            var rname = elem.GetProperty("iReturnField")[1].GetString();
            var body = elem.GetProperty("iFuncBody");

            var function = new Function();

            function.Name = name;

            var argc = args.GetArrayLength();
            for (int i = 0; i < argc; ++i) {
                function.Arguments.Add(function.GetOrDeclareVariable(args[i][1].GetString(), TypeToType(args[i][0].GetString())));
            }

            var block = function.NewBlock();
            function.Entry = block;

            var bodyc = body.GetArrayLength();
            var innerFun = new List<JsonElement>();
            for (int i = 0; i < bodyc; ++i) {
                if (body[i].GetProperty("tag").GetString() == "IRFun") {
                    innerFun.Add(body[i]);
                } else {
                    ProcessStatement(body[i], function, ref block);
                }
            }

            function.ReturnValue = function.GetOrDeclareVariable(rname, rtype);

            foreach (var pass in Passes) {
                while (pass(function)) { }
            }

            block.Condition = null;
            block.TrueNext = block.FalseNext = null;

            function.Output();

            innerFun.ForEach(ProcessFunction);
        }

        public static IList<Func<Function, bool>> Passes = new List<Func<Function, bool>>();

        public static void Main(string[] args) {
            string str = "", s;
            while ((s = Console.ReadLine()) != null) str += s;

            using var doc = JsonDocument.Parse(str);
            var root = doc.RootElement;
            var sectionCount = root.GetArrayLength();
            
            for (int i = 0; i < sectionCount; ++i) {
                var section = root[i];
                var sectionLength = section.GetArrayLength();

                for (int j = 0; j < sectionLength; ++j) {
                    var elem = section[j];

                    if (elem.ValueKind == JsonValueKind.String) {
                        Console.WriteLine(elem.ToString());
                    } else if (elem.ValueKind == JsonValueKind.Object) {
                        var tage = elem.GetProperty("tag");
                        var tag = tage.GetString();

                        switch (tag) {
                        case "IRExternFun":
                            break;
                        case "IRFun":
                            ProcessFunction(elem);
                            break;
                        default:
                            break;
                        }
                    }
                }
            }
        }
    }
}
