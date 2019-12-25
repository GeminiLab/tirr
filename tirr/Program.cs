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

            block.Condition = null;
            block.TrueNext = block.FalseNext = null;

            foreach (var pass in Passes) {
                var not_done = true;
                while (not_done) { (function, not_done) = pass(function); }
            }

            function.Output();

            innerFun.ForEach(ProcessFunction);
        }

        public static IList<Func<Function, (Function, bool)>> Passes =
            new List<Func<Function, (Function, bool)>>(
                new Func<Function, (Function, bool)>[] { 
                    (Function fun) => { // constant optimization
                        Console.WriteLine("constant opt...");
                        var changed = false;
                        for (int i = 0; i < fun.Blocks.Count; i ++) if(!fun.Blocks[i].Expired){
                            var block = fun.Blocks[i];
                            Dictionary<string, string> current_value = new Dictionary<string, string>();
                            for (int j = 0; j < block.Statements.Count; j ++) if(!block.Statements[j].Expired){
                                var stat = block.Statements[j];
                                Console.WriteLine($"{stat.GetType()} | {stat}");
                                if (stat.GetType() == typeof(SImm)) {
                                    // this means that we found a constant!
                                    var dname = ((SImm)stat).Destination.Name;
                                    current_value[dname] = ((SImm)stat).Immediate;
                                } else if (stat.GetType() == typeof(SAssign)) {
                                    var dname = ((SAssign)stat).Destination.Name;
                                    var sname = ((SAssign)stat).Source.Name;
                                    if (!current_value.ContainsKey(sname)
                                        || current_value[sname] == "<unknown>") {
                                        current_value[dname] = "<unknown>";
                                    } else {
                                        Console.WriteLine($"!!! {dname} === {current_value[sname]}");
                                        current_value[dname] = current_value[sname];
                                        block.Statements[j] = new SImm{
                                            Destination = ((SAssign)stat).Destination,
                                            Immediate = current_value[dname]
                                        };
                                    }
                                } else if (stat.GetType() == typeof(SBinary)) {
                                    var dname = ((SBinary)stat).Destination.Name;
                                    var o1name = ((SBinary)stat).Operand1.Name;
                                    var o2name = ((SBinary)stat).Operand2.Name;
                                    var op = ((SBinary)stat).Operation;
                                    if (!current_value.ContainsKey(o1name)
                                        || current_value[o1name] == "<unknown>"
                                        || !current_value.ContainsKey(o2name)
                                        || current_value[o2name] == "<unknown>") {
                                        current_value[dname] = "<unknown>";
                                    } else {
                                        Console.WriteLine
                                         ($"!!! {dname} === {current_value[o1name]} {op} {current_value[o2name]}");
                                        current_value[dname] = "<unknown>";
                                    }
                                } else if (stat.GetType() == typeof(SCast)){
                                    var dname = ((SCast)stat).Destination.Name;
                                    current_value[dname] = "<unknown>";
                                } else if (stat.GetType() == typeof(SCall)) {
                                    var dname = ((SCall)stat).Destination.Name;
                                    current_value[dname] = "<unknown>";
                                } else if (stat.GetType() == typeof(SDeref)) {
                                    var dname = ((SDeref)stat).Destination.Name;
                                    current_value[dname] = "<unknown>";
                                }
                            }
                            if (block.Condition != null) {
                                var name = block.Condition.Name;
                                if (current_value.ContainsKey(name) && current_value[name] != "<unknown>") {
                                    changed = true;
                                    block.FalseNext.Expired = block.TrueNext.Expired = true;
                                    if (current_value[name] != "0") {
                                        block.Statements.AddRange(block.TrueNext.Statements);
                                        block.Condition = block.TrueNext.Condition;
                                        block.FalseNext = block.TrueNext.FalseNext;
                                        block.TrueNext = block.TrueNext.TrueNext;
                                    } else {
                                        block.Statements.AddRange(block.FalseNext.Statements);
                                        block.Condition = block.FalseNext.Condition;
                                        block.TrueNext = block.FalseNext.TrueNext;
                                        block.FalseNext = block.FalseNext.FalseNext;
                                    }
                                }
                            }
                            fun.Blocks[i] = block;
                        }
                        return (fun, changed);
                    },
                    (Function fun) => { // faintness analysis
                        Dictionary<int, SortedSet<String>> input = new Dictionary<int,SortedSet<String>>();
                        for (int i = fun.Blocks.Count-1; i>=0; i--) if(!fun.Blocks[i].Expired){
                            var block = fun.Blocks[i];
                            SortedSet<String> c_input = new SortedSet<string>();
                            if (block.TrueNext == null) {
                                c_input.Add(fun.ReturnValue.Name);
                            } else {
                                c_input = input[block.TrueNext.GetHashCode()];
                                if (block.FalseNext != null) {
                                    c_input.UnionWith(input[block.FalseNext.GetHashCode()]);
                                }
                            }
                            for (int j = block.Statements.Count-1; j>=0; j--) if(!block.Statements[j].Expired){
                                var stat = block.Statements[j];
                                if (stat.GetType() == typeof(SImm)) {
                                    var dname = ((SImm)stat).Destination.Name;
                                    if (c_input.Contains(dname)) {
                                        c_input.Remove(dname);
                                    } else {
                                        stat.Expired = true;
                                    }
                                } else if (stat.GetType() == typeof(SAssign)) {
                                    var dname = ((SAssign)stat).Destination.Name;
                                    var sname = ((SAssign)stat).Source.Name;
                                    if (dname == sname || !c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else {
                                        c_input.Remove(dname);
                                        c_input.Add(sname);
                                    }
                                } else if (stat.GetType() == typeof(SBinary)) {
                                    var dname = ((SBinary)stat).Destination.Name;
                                    var o1name = ((SBinary)stat).Operand1.Name;
                                    var o2name = ((SBinary)stat).Operand2.Name;
                                    if (!c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else {
                                        c_input.Remove(dname);
                                        c_input.Add(o1name);
                                        c_input.Add(o2name);
                                    }
                                } else if (stat.GetType() == typeof(SCall)) {
                                    var dname = ((SCall)stat).Destination.Name;
                                    /* if (!c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else */ {
                                        c_input.Remove(dname);
                                        foreach (var v in ((SCall)stat).Arguments)
                                            c_input.Add(v.Name);
                                    }
                                } else if (stat.GetType() == typeof(SCast)) {
                                    var dname = ((SCast)stat).Destination.Name;
                                    var sname = ((SCast)stat).Source.Name;
                                    if (!c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else {
                                        c_input.Remove(dname);
                                        c_input.Add(sname);
                                    }
                                } else if (stat.GetType() == typeof(SDeref)) {
                                    var dname = ((SDeref)stat).Destination.Name;
                                    var sname = ((SDeref)stat).Address.Name;
                                    if (!c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else {
                                        c_input.Remove(dname);
                                        c_input.Add(sname);
                                    }
                                } else if (stat.GetType() == typeof(SModifyRef)) {
                                    var dname = ((SModifyRef)stat).Destination.Name;
                                    var sname = ((SModifyRef)stat).Source.Name;
                                    /* if (!c_input.Contains(dname)) {
                                        stat.Expired = true;
                                    } else */ {
                                        c_input.Remove(dname);
                                        c_input.Add(sname);
                                    }
                                }
                                block.Statements[j] = stat;
                            }
                            input[block.GetHashCode()] = c_input;
                            fun.Blocks[i] = block;
                        }
                        return (fun, false);
                    },
                });

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
