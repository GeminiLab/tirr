using System;
using System.Collections.Generic;

namespace tirr {
    public delegate bool Pass(ref Function fun);

    public static class PassLib {
        private static int TypeLength(string v) {
            return v switch {
                "int8_t" => 8,
                "int16_t" => 16,
                "int32_t" => 32,
                "int64_t" => 64,
                "uint8_t" => 8,
                "uint16_t" => 16,
                "uint32_t" => 32,
                "uint64_t" => 64,
                _ => 0,
            };
        }

        private static string EvaluateOp(string op, string o1, string o1t, string o2, string o2t) {
            var result = "<unknown>";
            unchecked {
                var o1_v = ulong.Parse(o1);
                var o2_v = ulong.Parse(o2);

                var (res, done) = op switch {
                    "+" => (o1_v + o2_v, true),
                    "-" => (o1_v - o2_v, true),
                    "*" => (o1_v * o2_v, true),
                    "/" => (o1_v / o2_v, true),
                    "==" => (o1_v == o2_v ? 1ul : 0ul, true),
                    "!=" => (o1_v != o2_v ? 1ul : 0ul, true),
                    ">" => (o1_v > o2_v ? 1ul : 0ul, true),
                    "<" => (o1_v < o2_v ? 1ul : 0ul, true),
                    ">=" => (o1_v >= o2_v ? 1ul : 0ul, true),
                    "<=" => (o1_v <= o2_v ? 1ul : 0ul, true),
                    _ => (0ul, false)
                };
                if (done) {
                    int lift_type = Math.Max(TypeLength(o1t), TypeLength(o2t));
                    result = $"{res & ((1ul << lift_type) - 1)}";
                }
            }
            return result;
        }

        public static bool ConstantFolding(ref Function fun) {
            //Console.WriteLine("constant opt...");
            var changed = false;
            var value = new Dictionary<int, Dictionary<string, string>>();
            for (int i = 0; i < fun.Blocks.Count; i++) {
                if (!fun.Blocks[i].Expired) {
                    var block = fun.Blocks[i];
                    var current_value = value.TryGetValue(block.GetHashCode(), out var d) ? d : value[block.GetHashCode()] = new Dictionary<string, string>();

                    for (int j = 0; j < block.Statements.Count; j++) {
                        if (!block.Statements[j].Expired) {
                            var stat = block.Statements[j];
                            switch (stat) {
                            case SImm imm: {
                                // this means that we found a constant!
                                var dname = imm.Destination.Name;
                                current_value[dname] = imm.Immediate;
                                break;
                            }
                            case SAssign assign: {
                                var dname = assign.Destination.Name;
                                var sname = assign.Source.Name;
                                if (!current_value.ContainsKey(sname) || current_value[sname] == "<unknown>") {
                                    current_value[dname] = "<unknown>";
                                } else {
                                    current_value[dname] = current_value[sname];
                                    block.Statements[j] = new SImm {
                                        Destination = assign.Destination,
                                        Immediate = current_value[dname]
                                    };
                                }

                                break;
                            }
                            case SBinary binary: {
                                var dname = binary.Destination.Name;
                                var o1name = binary.Operand1.Name;
                                var o1type = binary.Operand1.Type;
                                var o2name = binary.Operand2.Name;
                                var o2type = binary.Operand2.Type;
                                var op = binary.Operation;
                                if (!current_value.ContainsKey(o1name)
                                    || current_value[o1name] == "<unknown>"
                                    || !current_value.ContainsKey(o2name)
                                    || current_value[o2name] == "<unknown>") {
                                    current_value[dname] = "<unknown>";
                                } else {
                                    current_value[dname] = EvaluateOp(op, current_value[o1name], o1type, current_value[o2name], o2type);
                                    block.Statements[j] = new SImm {
                                        Destination = binary.Destination,
                                        Immediate = current_value[dname]
                                    };
                                }

                                break;
                            }
                            case SCast cast: {
                                var dname = cast.Destination.Name;
                                current_value[dname] = "<unknown>";
                                break;
                            }
                            case SCall call: {
                                var dname = call.Destination.Name;
                                current_value[dname] = "<unknown>";
                                break;
                            }
                            case SDeref deref: {
                                var dname = deref.Destination.Name;
                                current_value[dname] = "<unknown>";
                                break;
                            }
                            }
                        }
                    }
                    if (block.Condition != null) {
                        var name = block.Condition.Name;
                        if (current_value.ContainsKey(name) && current_value[name] != "<unknown>") {
                            changed = true;
                            block.Condition = null;
                            if (current_value[name] != "0") {
                                block.FalseNext.Expired = true;
                                block.FalseNext = null;
                            } else {
                                block.TrueNext.Expired = true;
                                block.TrueNext = null;
                            }
                        }
                    }
                    // fun.Blocks[i] = block;
                    if (block.TrueNext != null) {
                        var tv = value.TryGetValue(block.TrueNext.GetHashCode(), out var _d) ? _d : value[block.TrueNext.GetHashCode()] = new Dictionary<string, string>();
                        foreach (var (key, val) in current_value) {
                            if (!tv.ContainsKey(key)) tv[key] = val;
                            else if (tv[key] != val) tv[key] = "<unknown>";
                        }
                    }
                    if (block.FalseNext != null) {
                        var tv = value.TryGetValue(block.FalseNext.GetHashCode(), out var _d) ? _d : value[block.FalseNext.GetHashCode()] = new Dictionary<string, string>();
                        foreach (var (key, val) in current_value) {
                            if (!tv.ContainsKey(key)) tv[key] = val;
                            else if (tv[key] != val) tv[key] = "<unknown>";
                        }
                    }
                }
            }

            return changed;
        }

        public static bool FaintnessAnalysis(ref Function fun) { // faintness analysis
            var input = new Dictionary<int, SortedSet<string>>();
            var live = new SortedSet<string>();
            for (int i = fun.Blocks.Count - 1; i >= 0; i--) {
                if (!fun.Blocks[i].Expired) {
                    var block = fun.Blocks[i];
                    var c_input = new SortedSet<string>();
                    if (block.TrueNext == null) {
                        c_input.Add(fun.ReturnValue.Name);
                        live.Add(fun.ReturnValue.Name);
                    } else {
                        c_input = input[block.TrueNext.GetHashCode()];
                        if (block.FalseNext != null) {
                            c_input.UnionWith(input[block.FalseNext.GetHashCode()]);
                        }
                    }
                    for (int j = block.Statements.Count - 1; j >= 0; j--) {
                        if (!block.Statements[j].Expired) {
                            var stat = block.Statements[j];
                            switch (stat) {
                            case SImm imm: {
                                var dname = imm.Destination.Name;
                                if (c_input.Contains(dname)) {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                } else {
                                    imm.Expired = true;
                                }

                                break;
                            }
                            case SAssign assign: {
                                var dname = assign.Destination.Name;
                                var sname = assign.Source.Name;
                                if (dname == sname || !c_input.Contains(dname)) {
                                    assign.Expired = true;
                                } else {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                    c_input.Add(sname);
                                    live.Add(sname);
                                }

                                break;
                            }
                            case SBinary binary: {
                                var dname = binary.Destination.Name;
                                var o1name = binary.Operand1.Name;
                                var o2name = binary.Operand2.Name;
                                if (!c_input.Contains(dname)) {
                                    binary.Expired = true;
                                } else {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                    c_input.Add(o1name);
                                    live.Add(o1name);
                                    c_input.Add(o2name);
                                    live.Add(o2name);
                                }

                                break;
                            }
                            case SCall call: {
                                var dname = call.Destination.Name;
                                /* if (!c_input.Contains(dname)) {
                                stat.Expired = true;
                            } else */
                                {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                    foreach (var v in call.Arguments) {
                                        c_input.Add(v.Name);
                                        live.Add(v.Name);
                                    }
                                }
                                break;
                            }
                            case SCast cast: {
                                var dname = cast.Destination.Name;
                                var sname = cast.Source.Name;
                                if (!c_input.Contains(dname)) {
                                    cast.Expired = true;
                                } else {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                    c_input.Add(sname);
                                    live.Add(sname);
                                }

                                break;
                            }
                            case SDeref deref: {
                                var dname = deref.Destination.Name;
                                var sname = deref.Address.Name;
                                if (!c_input.Contains(dname)) {
                                    deref.Expired = true;
                                } else {
                                    c_input.Remove(dname);
                                    live.Add(dname);
                                    c_input.Add(sname);
                                    live.Add(sname);
                                }

                                break;
                            }
                            case SModifyRef @ref: {
                                var dname = @ref.Destination.Name;
                                var sname = @ref.Source.Name;
                                c_input.Remove(dname);
                                live.Add(dname);
                                c_input.Add(sname);
                                live.Add(sname);
                                break;
                            }
                            }
                            block.Statements[j] = stat;
                        }
                    }
                    input[block.GetHashCode()] = c_input;
                    fun.Blocks[i] = block;
                }
            }
            foreach (var (key, val) in fun.Variables) {
                val.Expired = !live.Contains(key);
            }

            // once is enough
            return false;
        }
    }
}