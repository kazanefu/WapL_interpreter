using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;



abstract record VariableValue;

record I32Value(int Data) : VariableValue;
record F32Value(float Data) : VariableValue;
record I64Value(long Data) : VariableValue;
record F64Value(double Data) : VariableValue;
record StringValue(string Data) : VariableValue;
record BoolValue(bool Data) : VariableValue;
record NullableValue(string Data) : VariableValue;
record PointerValue(int Data) : VariableValue;
record VecValue(VecPtrAndSize Data) : VariableValue;
record VecElements(List<VariableValue> Data) : VariableValue;
record IteratorValue(Iterator Data) : VariableValue;

class Iterator
{
    public VecElements element;
    public int size;
    public int pos;
    public void next()
    {
        pos++;
    }
    public VariableValue peek()
    {
        VariableValue ret = new NullableValue("");
        if (pos >= size) { return new NullableValue("Used up"); }
        switch (element)
        {
            case VecElements(var ve): ret = ve[pos]; break;
        }
        return ret;
    }
    public void setsize()
    {
        switch (element)
        {
            case VecElements(var ve): size = ve.Count; break;
        }
    }
    public void begin()
    {
        pos = 0;
    }
    public void end()
    {
        pos = size - 1;
    }
}

class VecPtrAndSize { public int ptr; public int len; public int capacity; }
class EmptyArea
{
    public int start;
    public int size;
    public int end()
    {
        return (start + size - 1);
    }
}
class Memory
{
    public VariableValue[] memory;
    public List<EmptyArea> emptyArea;
}
class Variable
{
    public string Type; // "i32","i64","f64","f32", "String", "bool","ptr","vec","iter"
    public int ptr;
    public VariableValue Value;
}

class Function
{
    public List<(string Type, string Name)> Parameters = new List<(string, string)>();
    public List<string> Body = new List<string>();
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("対話形式で開始します:exit();すると終了します");
            chatmode();
            return;
        }
        string path = args[0];

        if (!File.Exists(path))
        {
            Console.WriteLine($"ファイルが見つかりません: {path}");
            return;
        }
        string all = File.ReadAllText(path);
        var interpreter = new WapLInterpreter();
        interpreter.ReadInputFromString(all);
        interpreter.RunCode();
    }
    static void chatmode()
    {
        var interpreter = new WapLInterpreter();
        string all = Console.ReadLine() ?? "";
        interpreter.ReadInputFromString(all);
        interpreter.RunCode();
        while (true)
        {
            Console.Write(">>");
            string input_value = Console.ReadLine() ?? "";
            if(input_value.Trim() == "exit();") { break; }
            else if(input_value.Trim() == "refresh();") { interpreter.ReadInput(); }
            else {
                interpreter.unrefreshRead(input_value);
                interpreter.RunCode();
            }
        }
    }
}
public class WapLInterpreter
{

    Dictionary<string, Stopwatch> timers = new Dictionary<string, Stopwatch>();
    Dictionary<string, Variable> variables = new Dictionary<string, Variable>();
    Dictionary<string, Function> functions = new Dictionary<string, Function>();
    Dictionary<string, int> labelPositions = new Dictionary<string, int>();
    Memory vmemory = new Memory { memory = new VariableValue[10000], emptyArea = new List<EmptyArea>() };
    public string input;
    public string[] commands;
    public bool first_call;
    
    public void ReadInput()
    {
        refresh();
    }
    public void ReadInputFromString(string code)
    {
        input = code;
        refresh();
    }
    public void unrefreshRead(string code)
    {
        input = code;
        commands = input.Split(';');
    }
    void refresh()
    {
        first_call = true;
        timers = new Dictionary<string, Stopwatch>();
        functions = new Dictionary<string, Function>();
        variables = new Dictionary<string, Variable>();
        labelPositions = new Dictionary<string, int>();
        vmemory = new Memory { memory = new VariableValue[10000], emptyArea = new List<EmptyArea>() };
        for (int i = 0; i < 10000; i++)
        {
            vmemory.memory[i] = new NullableValue("");
        }
        vmemory.emptyArea.Add(new EmptyArea { start = 0, size = 10000 });
        commands = input.Split(';');
    }

    public float RunCode()
    {
        float used_energy = 0.0f;
        Dictionary<string, int> labelLimit = new Dictionary<string, int>();

        // ラベル位置のスキャン
        for (int i = 0; i < commands.Length; i++)
        {
            string line = commands[i].Trim();
            if (line.StartsWith("point "))
            {
                string labelName = line.Substring(6).Trim();
                labelPositions[labelName] = i;
                labelLimit[labelName] = 0;
            }
        }
        //関数のスキャン
        for (int i = 0; i < commands.Length; i++)
        {
            string trimmed = commands[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("fn "))
            {
                string head = trimmed.Substring(3);
                int lparen = head.IndexOf('(');
                int rparen = head.IndexOf(')');
                string funcName = head.Substring(0, lparen).Trim();
                string argsPart = head.Substring(lparen + 1, rparen - lparen - 1);

                var parameters = new List<(string, string)>();
                foreach (var p in argsPart.Split(','))
                {
                    var parts = p.Trim().Split(' ');
                    if (parts.Length == 2)
                    {
                        parameters.Add((parts[0], parts[1]));
                    }
                }

                List<string> body = new List<string>();
                i++;
                while (i < commands.Length && !commands[i].Trim().StartsWith("}"))
                {
                    body.Add(commands[i].Trim());
                    i++;
                }
                functions[funcName] = new Function
                {
                    Parameters = parameters,
                    Body = body
                };
                continue;
            }
        }

        for (int i = 0; i < commands.Length; i++)
        {
            string trimmed = commands[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("fn "))
            {
                string head = trimmed.Substring(3);
                int lparen = head.IndexOf('(');
                int rparen = head.IndexOf(')');
                string funcName = head.Substring(0, lparen).Trim();
                string argsPart = head.Substring(lparen + 1, rparen - lparen - 1);

                var parameters = new List<(string, string)>();
                foreach (var p in argsPart.Split(','))
                {
                    var parts = p.Trim().Split(' ');
                    if (parts.Length == 2)
                    {
                        parameters.Add((parts[0], parts[1]));
                    }
                }

                List<string> body = new List<string>();
                i++;
                while (i < commands.Length && !commands[i].Trim().StartsWith("}"))
                {
                    body.Add(commands[i].Trim());
                    i++;
                }
                continue;
            }

            if (trimmed.StartsWith("warpto("))
            {
                string labelName = trimmed.Substring(7, trimmed.Length - 8).Trim();
                if (labelPositions.ContainsKey(labelName))
                {
                    if (labelLimit[labelName] >= 10000000)
                    {
                        Console.WriteLine("無限ループの可能性があります: " + labelName);
                        continue;
                    }
                    i = labelPositions[labelName];
                    labelLimit[labelName] += 1;
                    continue;
                }
                else
                {
                    Console.WriteLine("ラベルが見つかりません: " + labelName);
                }
            }

            // warptoif(条件, ラベル名) の処理
            if (trimmed.StartsWith("warptoif("))
            {
                string inner = trimmed.Substring(9, trimmed.Length - 10);
                string[] parts = SplitArgs(inner);
                if (parts.Length == 2)
                {
                    bool conditionResult = false;
                    switch (EvaluateExpression(parts[0].Trim()))
                    {
                        case BoolValue(var b): conditionResult = b; break;
                    }

                    string label = parts[1].Trim();
                    if (conditionResult == true && labelPositions.ContainsKey(label))
                    {
                        if (labelLimit[label] >= 10000000)
                        {
                            Console.WriteLine("無限ループの可能性があります: " + label);
                            continue;
                        }
                        i = labelPositions[label];
                        labelLimit[label] += 1;
                        continue;
                    }
                }
            }

            EvaluateCommand(trimmed);
        }


        first_call = false;
        return used_energy;

    }

#nullable enable
    VariableValue EvaluateCommand(string line, Dictionary<string, Variable>? localScope = null)
    {
        return EvaluateExpression(line, localScope);
    }
    VariableValue ExecuteFunctionBody(List<string> body, Dictionary<string, Variable> scope)
    {
        // 関数内だけのラベル表
        var localLabels = new Dictionary<string, int>();
        var localLabellimits = new Dictionary<string, int>();
        for (int i = 0; i < body.Count; i++)
        {
            string line = body[i].Trim();
            if (line.StartsWith("point "))
            {
                string labelName = line.Substring(6).Trim();
                localLabels[labelName] = i;
                localLabellimits[labelName] = 0;
            }
        }

        for (int i = 0; i < body.Count; i++)
        {
            string line = body[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("warpto("))
            {
                string labelName = line.Substring(7, line.Length - 8).Trim();
                if (localLabels.ContainsKey(labelName))
                {
                    if (localLabellimits[labelName] >= 10000000)
                    {
                        Console.WriteLine("無限ループの可能性があります: " + labelName);
                        continue;
                    }
                    i = localLabels[labelName];
                    localLabellimits[labelName] += 1;
                    continue;
                }
                else
                {
                    Console.WriteLine("関数内のラベルが見つかりません: " + labelName);
                }
            }

            if (line.StartsWith("warptoif("))
            {
                string inner = line.Substring(9, line.Length - 10);
                string[] parts = SplitArgs(inner);
                if (parts.Length == 2)
                {
                    bool condition = false;
                    switch (EvaluateExpression(parts[0].Trim(), scope))
                    {
                        case BoolValue(var b): condition = b; break;
                    }
                    string label = parts[1].Trim();
                    if (condition == true && localLabels.ContainsKey(label))
                    {
                        if (localLabellimits[label] >= 10000000)
                        {
                            Console.WriteLine("無限ループの可能性があります: " + label);
                            continue;
                        }
                        i = localLabels[label];
                        continue;
                    }
                }
            }


            if (line.StartsWith("return "))
            {
                string retExpr = line.Substring(7).Trim();
                VariableValue ret = EvaluateExpression(retExpr, scope);
                foreach (var j in scope)
                {
                    if (!j.Key.StartsWith("&_")) { free(j.Value.ptr, 1); }
                }
                return ret; // ← ここで値を返す
            }

            EvaluateCommand(line, scope);
        }
        return new NullableValue("");
    }
    VariableValue EvaluateExpression(string exprInput, Dictionary<string, Variable>? scope = null)
    {
        exprInput = exprInput.Trim();
        if (exprInput.StartsWith("\"") && exprInput.EndsWith("\"")) return new StringValue(exprInput.Substring(1, exprInput.Length - 2).Replace("\\n", "\n"));
        if (double.TryParse(exprInput, out double n)) return new F64Value(n);
        if (exprInput == "true") { return new BoolValue(true); } else if (exprInput == "false") { return new BoolValue(false); }
        if ((scope != null && scope.ContainsKey(exprInput))) { scope[exprInput].Value = vmemory.memory[scope[exprInput].ptr]; return scope[exprInput].Value; }
        if (variables.ContainsKey(exprInput)) { variables[exprInput].Value = vmemory.memory[variables[exprInput].ptr]; return variables[exprInput].Value; }

        if (exprInput.Contains("(") && exprInput.EndsWith(")"))
        {
            int lparen = exprInput.IndexOf('(');
            string op = exprInput.Substring(0, lparen);
            string inside = exprInput.Substring(lparen + 1, exprInput.Length - lparen - 2);
            string[] parts = SplitArgs(inside);

            List<VariableValue> evalpart = new List<VariableValue>(parts.Length);
            if (op != "do" && op != "if" && op != "iter")
            {
                for (int l = 0; l < parts.Length; l++)
                {
                    evalpart.Add(parts.Length > l ? EvaluateExpression(parts[l], scope) : new NullableValue(""));
                }
            }
            switch (op)
            {
                case "+": return TypeAjust(TypeReturn(evalpart[0]), new F64Value(VariableToDouble(evalpart[0]) + VariableToDouble(evalpart[1])));
                case "t+": return new StringValue(VariableToString(To_String(evalpart[0])) + VariableToString(To_String(evalpart[1])));
                case "-": return TypeAjust(TypeReturn(evalpart[0]), new F64Value(VariableToDouble(evalpart[0]) - VariableToDouble(evalpart[1])));
                case "*": return TypeAjust(TypeReturn(evalpart[0]), new F64Value(VariableToDouble(evalpart[0]) * VariableToDouble(evalpart[1])));
                //case "t*": string textadd = ""; for (int i = 1; i <= int.Parse(evalpart[1]); i++) { textadd += evalpart[0]; } return textadd;
                case "/": return VariableToDouble(evalpart[1]) != 0 ? TypeAjust(TypeReturn(evalpart[0]), new F64Value(VariableToDouble(evalpart[0]) / VariableToDouble(evalpart[1]))) : TypeAjust(TypeReturn(evalpart[0]), new F64Value(0));
                case "%": return VariableToDouble(evalpart[1]) != 0 ? TypeAjust(TypeReturn(evalpart[0]), new F64Value(VariableToDouble(evalpart[0]) % VariableToDouble(evalpart[1]))) : TypeAjust(TypeReturn(evalpart[0]), new F64Value(0));
                case "==": return new BoolValue(evalpart[0] == evalpart[1]);
                case "!=": return new BoolValue(!(evalpart[0] == evalpart[1]));
                case ">": return new BoolValue(VariableToDouble(evalpart[0]) > VariableToDouble(evalpart[1]));
                case "<": return new BoolValue(VariableToDouble(evalpart[0]) < VariableToDouble(evalpart[1]));
                case ">=": return new BoolValue(VariableToDouble(evalpart[0]) >= VariableToDouble(evalpart[1]));
                case "<=": return new BoolValue(VariableToDouble(evalpart[0]) <= VariableToDouble(evalpart[1]));
                case "and": return new BoolValue(VariableToBool(evalpart[0]) && VariableToBool(evalpart[1]));
                case "or": return new BoolValue(VariableToBool(evalpart[0]) || VariableToBool(evalpart[1]));
                case "not": return new BoolValue(!VariableToBool(evalpart[0]));
                case "&&": return new BoolValue(VariableToBool(evalpart[0]) && VariableToBool(evalpart[1]));
                case "||": return new BoolValue(VariableToBool(evalpart[0]) || VariableToBool(evalpart[1]));
                case "!": return new BoolValue(!VariableToBool(evalpart[0]));
                case "+=": if ((scope != null && scope.ContainsKey(parts[0]))) { SetVariable(parts[0], scope[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) + VariableToDouble(evalpart[1])), scope); } else if (variables.ContainsKey(parts[0])) { SetVariable(parts[0], variables[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) + VariableToDouble(evalpart[1])), null); } return new F64Value(VariableToDouble(evalpart[0]) + VariableToDouble(evalpart[1]));
                case "-=": if ((scope != null && scope.ContainsKey(parts[0]))) { SetVariable(parts[0], scope[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) - VariableToDouble(evalpart[1])), scope); } else if (variables.ContainsKey(parts[0])) { SetVariable(parts[0], variables[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) - VariableToDouble(evalpart[1])), null); } return new F64Value(VariableToDouble(evalpart[0]) - VariableToDouble(evalpart[1]));
                case "*=": if ((scope != null && scope.ContainsKey(parts[0]))) { SetVariable(parts[0], scope[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) * VariableToDouble(evalpart[1])), scope); } else if (variables.ContainsKey(parts[0])) { SetVariable(parts[0], variables[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) * VariableToDouble(evalpart[1])), null); } return new F64Value(VariableToDouble(evalpart[0]) * VariableToDouble(evalpart[1]));
                case "/=": if (VariableToDouble(evalpart[1]) != 0) { if ((scope != null && scope.ContainsKey(parts[0]))) { SetVariable(parts[0], scope[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) / VariableToDouble(evalpart[1])), scope); } else if (variables.ContainsKey(parts[0])) { SetVariable(parts[0], variables[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) / VariableToDouble(evalpart[1])), null); } return new F64Value(VariableToDouble(evalpart[0]) / VariableToDouble(evalpart[1])); } return new F64Value(0);
                case "%=": if (VariableToDouble(evalpart[1]) != 0) { if ((scope != null && scope.ContainsKey(parts[0]))) { SetVariable(parts[0], scope[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) % VariableToDouble(evalpart[1])), scope); } else if (variables.ContainsKey(parts[0])) { SetVariable(parts[0], variables[parts[0]].Type, new F64Value(VariableToDouble(evalpart[0]) % VariableToDouble(evalpart[1])), null); } return new F64Value(VariableToDouble(evalpart[0]) % VariableToDouble(evalpart[1])); } return new F64Value(0);
                case "=":
                    string type = "String";
                    if (parts.Length < 3) { type = TypeReturn(evalpart[0]); } else { type = VariableToString(evalpart[2]); }
                    string name = parts[0].Trim();  // parts[0]
                    VariableValue value = evalpart[1]; // parts[1]
                    SetVariable(name, type, value, scope);
                    return value;
                case "split":
                    string[] splitedstrs = VariableToString(To_String(evalpart[0])).Split(VariableToString(To_String(evalpart[1])));
                    List<VariableValue> strelements = new();
                    foreach(string e in splitedstrs)
                    {
                        strelements.Add(new StringValue(e));
                    }
                    return new VecElements(strelements);
                case "trim":
                    string sourcestr = VariableToString(To_String(evalpart[0]));
                    sourcestr = sourcestr.Trim();
                    return new StringValue(sourcestr);
                case "input":
                    string input_name = VariableToString(To_String(evalpart[0]));
                    if(input_name != "")
                    {
                        Console.Write($"{input_name}");
                    }
                    string input_value = Console.ReadLine() ?? "";
                    return new StringValue(input_value);
                case "print":
                    for (int i = 0; i <= parts.Length - 1; i++)
                    {
                        Console.Write(VariableToString(To_String(evalpart[i])));
                    }
                    return evalpart[0];
                case "println":
                    for (int i = 0; i <= parts.Length - 1; i++)
                    {
                        Console.WriteLine(VariableToString(To_String(evalpart[i])));
                    }

                    return evalpart[0];
                case "if":
                    evalpart.Add(EvaluateExpression(parts[0], scope));
                    if (VariableToBool(evalpart[0]) == true) { return EvaluateExpression(parts[1], scope); } else { return EvaluateExpression(parts[2], scope); }
                case "do":
                    var localVars = new Dictionary<string, Variable>();
                    List<string> todo = new List<string>();
                    for (int i = 0; i <= parts.Length - 1; i++)
                    {
                        if (parts[i].StartsWith("takein("))
                        {
                            int do_lparen = parts[i].IndexOf('(');
                            string do_op = parts[i].Substring(0, do_lparen);
                            string do_inside = parts[i].Substring(do_lparen + 1, parts[i].Length - do_lparen - 2);
                            string[] do_parts = SplitArgs(do_inside);
                            foreach (string quote in do_parts)
                            {
                                if ((scope != null && scope.ContainsKey(quote)))
                                {
                                    localVars[quote] = scope[quote];
                                }
                                else if (variables.ContainsKey(quote))
                                {
                                    localVars[quote] = variables[quote];
                                }
                                else { return new NullableValue("no defined variable"); }
                            }
                        }
                        else
                        {
                            todo.Add(parts[i].Trim());
                        }
                    }
                    VariableValue result = ExecuteFunctionBody(todo, localVars);
                    return result;
                case "expel":
                    if (scope != null && scope.ContainsKey(parts[0]))
                    {
                        return new BoolValue(scope.Remove(parts[0]));
                    }
                    else
                    {
                        return new NullableValue("can't find this variable in this scope");
                    }
                case "ptr":
                    if ((scope != null && scope.ContainsKey(parts[0].Trim()))) { return new PointerValue(scope[parts[0].Trim()].ptr); }
                    else if (variables.ContainsKey(parts[0].Trim())) { return new PointerValue(variables[parts[0].Trim()].ptr); }
                    else { return new NullableValue("not variable"); }
                case "vec_start":
                    switch (evalpart[0]) { case VecValue(var vv): return new PointerValue(vv.ptr); }
                    return new NullableValue("no ptr");
                case "to_ptr": return new PointerValue((int)VariableToDouble(evalpart[0]));
                case "alias":
                    int a_ptr = (int)VariableToDouble(evalpart[1]);
                    string a_type = "String";
                    string a_name = parts[0].Trim();
                    VariableValue a_value = evalpart[1];
                    SetVariableWithPtr(a_ptr, a_name, a_type, vmemory.memory[a_ptr], scope);
                    return a_value;
                case "val": return vmemory.memory[(int)VariableToDouble(evalpart[0])];
                case "chars":
                    char[] chars = VariableToString(evalpart[0]).ToCharArray();
                    List<VariableValue> charcontents = new List<VariableValue>();
                    foreach (char Char in chars)
                    {
                        charcontents.Add(new StringValue(Char.ToString()));
                    }
                    return new VecElements(charcontents);
                case "vec": List<VariableValue> ret = new List<VariableValue>(); for (int i = 0; i < evalpart.Count; i++) { if (evalpart.Count == 1 && parts[0].Trim() == "") { } else { ret.Add(evalpart[i]); } } return new VecElements(ret);
                case "rangei":
                    int rangeiStart = 0;
                    int rangeiEnd = 0;
                    int rangeiStep = 1;
                    if (parts.Length == 1)
                    {
                        rangeiEnd = (int)VariableToDouble(evalpart[0]);
                    }
                    else if (parts.Length == 2)
                    {
                        rangeiStart = (int)VariableToDouble(evalpart[0]);
                        rangeiEnd = (int)VariableToDouble(evalpart[1]);
                    }
                    else if (parts.Length == 3)
                    {
                        rangeiStart = (int)VariableToDouble(evalpart[0]);
                        rangeiEnd = (int)VariableToDouble(evalpart[1]);
                        rangeiStep = (int)VariableToDouble(evalpart[2]);
                    }
                    List<VariableValue> rangeiret = new List<VariableValue>();
                    for (int i = rangeiStart; i < rangeiEnd; i += rangeiStep)
                    {
                        rangeiret.Add(new I32Value(i));
                    }
                    return new VecElements(rangeiret);
                case "rangef":
                    double rangefStart = 0.0;
                    double rangefEnd = 0.0;
                    double rangefStep = 1.0;
                    if (parts.Length == 1)
                    {
                        rangefEnd = VariableToDouble(evalpart[0]);
                    }
                    else if (parts.Length == 2)
                    {
                        rangefStart = VariableToDouble(evalpart[0]);
                        rangefEnd = VariableToDouble(evalpart[1]);
                    }
                    else if (parts.Length == 3)
                    {
                        rangefStart = VariableToDouble(evalpart[0]);
                        rangefEnd = VariableToDouble(evalpart[1]);
                        rangefStep = VariableToDouble(evalpart[2]);
                    }
                    List<VariableValue> rangefret = new List<VariableValue>();
                    for (double i = rangefStart; i < rangefEnd; i += rangefStep)
                    {
                        rangefret.Add(new F64Value(i));
                    }
                    return new VecElements(rangefret);
                case "expand":
                    List<VariableValue> contents = new List<VariableValue>();
                    switch (evalpart[0])
                    {
                        case VecValue(var vv):
                            for (int i = 0; i < vv.len; i++)
                            {
                                contents.Add(vmemory.memory[vv.ptr + i]);
                            }
                            break;
                    }
                    return new VecElements(contents);
                case "=ptr":
                    vmemory.memory[(int)VariableToDouble(evalpart[0])] = evalpart[1];
                    return evalpart[1];
                case "as":
                    string as_type = parts[0].Trim();
                    if (TypeReturn(evalpart[0]) == "String") { as_type = VariableToString(evalpart[0]); }
                    return TypeAjust(as_type, evalpart[1]);
                case "get_at": switch (evalpart[0]) { case VecValue(var vv): int start_ptr = vv.ptr; return vmemory.memory[(int)VariableToDouble(evalpart[1]) + start_ptr]; } return new NullableValue("not vec");
                case "clear":
                    switch (evalpart[0])
                    {
                        case VecValue(var vv): return new VecValue(new VecPtrAndSize { ptr = vv.ptr, len = 0, capacity = vv.capacity });
                    }
                    return new NullableValue("not vec");
                case "len":
                    switch (evalpart[0]) { case VecValue(var vv): return new I32Value(vv.len); }
                    return new NullableValue("not vec");
                case "push":
                    List<VariableValue> push_contents = new List<VariableValue>();
                    switch (evalpart[0])
                    {
                        case VecValue(var vv):
                            for (int i = 0; i < vv.len; i++)
                            {
                                push_contents.Add(vmemory.memory[vv.ptr + i]);
                            }
                            if (vv.len + 1 > vv.capacity)
                            {
                                push_contents.Add(evalpart[1]);
                                free(vv.ptr, vv.len);
                                SetVariable(parts[0].Trim(), "vec", new VecElements(push_contents));
                                return new VecValue(new VecPtrAndSize { ptr = vv.ptr, len = vv.len + 1, capacity = vv.len + 1 });
                            }
                            else
                            {
                                vmemory.memory[vv.ptr + vv.len] = evalpart[1];
                                return new VecValue(new VecPtrAndSize { ptr = vv.ptr, len = vv.len + 1, capacity = vv.capacity });
                            }

                    }
                    return new NullableValue("you can't push because it is not vec");
                case "malloc":
                    int mallocptr = alloc((int)VariableToDouble(evalpart[0]));
                    return new PointerValue(mallocptr);
                case "memcpy":
                    int dest = (int)VariableToDouble(evalpart[0]);
                    int src = (int)VariableToDouble(evalpart[1]);
                    for (int i = 0; i < (int)VariableToDouble(evalpart[2]); i++)
                    {
                        vmemory.memory[dest + i] = vmemory.memory[src + i];
                    }
                    return evalpart[0];
                case "memset":
                    int setdest = (int)VariableToDouble(evalpart[0]);
                    VariableValue setval = evalpart[1];
                    for (int i = 0; i < (int)VariableToDouble(evalpart[2]); i++)
                    {
                        vmemory.memory[setdest + i] = setval;
                    }
                    return evalpart[0];
                case "free":
                    switch (evalpart[0])
                    {
                        case VecValue(var vv): free(vv.ptr, vv.capacity); break;
                        case PointerValue(var p):
                            if (evalpart.Count == 2)
                            {
                                free(p, (int)VariableToDouble(evalpart[1]));
                            }
                            break;
                    }
                    if ((scope != null && scope.ContainsKey(parts[0].Trim()))) { free(scope[parts[0].Trim()].ptr, 1); return new PointerValue(scope[parts[0].Trim()].ptr); }
                    else if (variables.ContainsKey(parts[0].Trim())) { free(variables[parts[0].Trim()].ptr, 1); return new PointerValue(variables[parts[0].Trim()].ptr); }
                    else { return new NullableValue("not variable"); }
                case "type":
                    return new StringValue(TypeReturn(evalpart[0]));
                case "is_first":
                    return new BoolValue(first_call);
                case "set_timer":
                    timers[VariableToString(evalpart[0])] = new Stopwatch();
                    timers[VariableToString(evalpart[0])].Restart();
                    return evalpart[0];
                case "get_timer":
                    if (timers.ContainsKey(VariableToString(evalpart[0])))
                    {
                        return new F64Value(timers[VariableToString(evalpart[0])].Elapsed.TotalSeconds);
                    }
                    return evalpart[0];
                case "next":
                    switch (evalpart[0])
                    {
                        case IteratorValue(var iter): iter.next(); return iter.peek();
                    }
                    return new NullableValue("not iterator");
                case "peek":
                    switch (evalpart[0])
                    {
                        case IteratorValue(var iter): return iter.peek();
                    }
                    return new NullableValue("not iterator");
                case "begin":
                    switch (evalpart[0])
                    {
                        case IteratorValue(var iter): iter.begin(); return iter.peek();
                    }
                    return new NullableValue("not iterator");
                case "end":
                    switch (evalpart[0])
                    {
                        case IteratorValue(var iter): iter.end(); return iter.peek();
                    }
                    return new NullableValue("not iterator");
                case "iter":
                    switch (EvaluateExpression(parts[0].Trim(), scope))
                    {
                        case VecElements(var ve):
                            List<VariableValue> collection = new List<VariableValue>(ve.Count);
                            Iterator elements = new Iterator { element = new VecElements(ve), size = ve.Count, pos = 0 };
                            for (int i = 1; i < parts.Length; i++)
                            {
                                var localVars_iter = new Dictionary<string, Variable>();
                                collection.Clear();
                                string typename = "";
                                string valname = "_";
                                if (parts[i].Contains("(") && parts[i].EndsWith(")"))
                                {
                                    int lparen_iter = parts[i].IndexOf('(');
                                    string op_iter = parts[i].Substring(0, lparen_iter);
                                    string inside_iter = parts[i].Substring(lparen_iter + 1, parts[i].Length - lparen_iter - 2);
                                    string[] parts_iter = SplitArgs(inside_iter);
                                    valname = parts_iter[0].Trim();
                                    switch (op_iter)
                                    {
                                        case "map":
                                            //foreach(var x in elements)
                                            while (elements.pos < elements.size)
                                            {
                                                var x = elements.peek();
                                                typename = TypeReturn(x);
                                                SetVariable(valname, typename, x, localVars_iter);
                                                collection.Add(EvaluateExpression(parts_iter[1], localVars_iter));
                                                foreach (var f in localVars_iter)
                                                {
                                                    string f_type = f.Value.Type;
                                                    int f_ptr = f.Value.ptr;
                                                    if (!f.Key.StartsWith("&_"))
                                                    {
                                                        if (f_type == "vec")
                                                        {
                                                            switch (f.Value.Value)
                                                            {
                                                                case VecValue(var vv):
                                                                    free(vv.ptr, vv.capacity);
                                                                    break;
                                                            }
                                                        }
                                                        free(f_ptr, 1);
                                                    }

                                                }
                                                localVars_iter = new Dictionary<string, Variable>();
                                                elements.next();
                                            }
                                            var element_element_list = new List<VariableValue>(collection);
                                            var element_element = new VecElements(element_element_list);
                                            elements = new Iterator { element = element_element, size = element_element_list.Count, pos = 0 };
                                            collection.Clear();
                                            break;
                                        case "rev":
                                            while (elements.pos < elements.size)
                                            {
                                                var x = elements.peek();
                                                collection.Add(x);
                                                elements.next();
                                            }
                                            collection.Reverse();
                                            var rev_list = new List<VariableValue>(collection);
                                            var element_rev = new VecElements(rev_list);
                                            elements = new Iterator { element = element_rev, size = rev_list.Count, pos = 0 };
                                            collection.Clear();
                                            break;
                                        case "filter":
                                            while (elements.pos < elements.size)
                                            {
                                                var x = elements.peek();
                                                typename = TypeReturn(x);
                                                SetVariable(valname, typename, x, localVars_iter);
                                                var bool_result = EvaluateExpression(parts_iter[1], localVars_iter);
                                                if (VariableToBool(bool_result)) { collection.Add(x); }
                                                foreach (var f in localVars_iter)
                                                {
                                                    string f_type = f.Value.Type;
                                                    int f_ptr = f.Value.ptr;
                                                    if (!f.Key.StartsWith("&_"))
                                                    {
                                                        if (f_type == "vec")
                                                        {
                                                            switch (f.Value.Value)
                                                            {
                                                                case VecValue(var vv):
                                                                    free(vv.ptr, vv.capacity);
                                                                    break;
                                                            }
                                                        }
                                                        free(f_ptr, 1);
                                                    }

                                                }
                                                localVars_iter = new Dictionary<string, Variable>();
                                                elements.next();
                                            }
                                            var filter_list = new List<VariableValue>(collection);
                                            var filter_element = new VecElements(filter_list);
                                            elements = new Iterator { element = filter_element, size = filter_list.Count, pos = 0 };
                                            collection.Clear();
                                            break;
                                    }
                                }

                            }
                            return elements.element;
                    }
                    return new NullableValue("not iter");
            }

            if (functions.ContainsKey(op))
            {
                var func = functions[op];
                var localVars = new Dictionary<string, Variable>();
                for (int j = 0; j < func.Parameters.Count; j++)
                {
                    VariableValue val = EvaluateExpression(parts[j].Trim(), scope);
                    if (func.Parameters[j].Name.StartsWith("&_"))
                    {
                        int ptr = (int)VariableToDouble(val);
                        SetVariableWithPtr(ptr, func.Parameters[j].Name, func.Parameters[j].Type, vmemory.memory[ptr], localVars);
                    }
                    else
                    {
                        SetVariable(func.Parameters[j].Name, func.Parameters[j].Type, val, localVars);
                        //localVars[func.Parameters[j].Name] = new Variable { Type = func.Parameters[j].Type, Value = val };
                    }
                }
                VariableValue result = ExecuteFunctionBody(func.Body, localVars);
                return result;
            }
        }
        return new StringValue(exprInput.Trim());
    }

    string[] SplitArgs(string input)
    {
        List<string> args = new List<string>();
        int depth = 0, start = 0,quotation = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '(') depth++;
            else if (input[i] == ')') depth--;
            else if (input[i] == '"') quotation++;
            else if (input[i] == ',' && depth == 0 && quotation%2 == 0)
            {
                args.Add(input.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        args.Add(input.Substring(start).Trim());
        return args.ToArray();
    }

    void SetVariable(string name, string type, VariableValue value, Dictionary<string, Variable>? scope = null)
    {
        if (type.StartsWith("gbl_"))
        {
            scope = null;
            type = type.Substring(4);
        }
        int capa = 0;
        if (type.StartsWith("vec_"))
        {
            if (int.TryParse(type.Substring(4).Trim(), out int i)) { capa = i; }
            type = "vec";
        }
        VariableValue value_new = TypeAjust(type, value);
        int size = 1;
        if (type == "vec")
        {
            switch (TypeReturn(value_new))
            {
                case "vec": break;
                case "vece":
                    switch (value_new)
                    {
                        case VecElements(var ve):
                            size = ve.Count();
                            int size_capa = (size > capa) ? size : capa;
                            int ptr = alloc(size_capa);
                            for (int i = 0; i < size; i++)
                            {
                                vmemory.memory[ptr + i] = ve[i];
                            }
                            value_new = new VecValue(new VecPtrAndSize { ptr = ptr, len = size, capacity = size_capa });
                            size = 1;
                            break;
                    }
                    break;
                case "iter":
                    switch (value_new)
                    {
                        case IteratorValue(var ie):
                            switch (ie.element)
                            {
                                case VecElements(var ve):
                                    size = ve.Count();
                                    int size_capa = (size > capa) ? size : capa;
                                    int ptr = alloc(size_capa);
                                    for (int i = 0; i < size; i++)
                                    {
                                        vmemory.memory[ptr + i] = ve[i];
                                    }
                                    value_new = new VecValue(new VecPtrAndSize { ptr = ptr, len = size, capacity = size_capa });
                                    size = 1;
                                    break;
                            }
                            break;
                    }
                    break;
            }
        }
        if (scope != null)
        {
            if (scope.ContainsKey(name))
            {
                scope[name] = new Variable { Type = type, Value = value_new, ptr = scope[name].ptr };
            }
            else
            {
                int pointer = alloc(size);
                scope[name] = new Variable { Type = type, Value = value_new, ptr = pointer };
            }
            vmemory.memory[scope[name].ptr] = value_new;
        }
        else
        {
            if (variables.ContainsKey(name))
            {
                variables[name] = new Variable { Type = type, Value = value_new, ptr = variables[name].ptr };
            }
            else
            {
                int pointer = alloc(size);
                variables[name] = new Variable { Type = type, Value = value_new, ptr = pointer };
            }
            vmemory.memory[variables[name].ptr] = value_new;

        }

    }
    void SetVariableWithPtr(int ptr, string name, string type, VariableValue value, Dictionary<string, Variable>? scope = null)
    {
        VariableValue value_new = TypeAjust(type, value);
        if (scope != null)
        {
            scope[name] = new Variable { Type = type, Value = value_new, ptr = ptr };
        }
        else
        {
            variables[name] = new Variable { Type = type, Value = value_new, ptr = ptr };
        }

    }

    double VariableToDouble(VariableValue value)
    {
        double val = 0.0;
        switch (value)
        {
            case I32Value(var i): val = i; break;
            case I64Value(var l): val = l; break;
            case F32Value(var f): val = f; break;
            case F64Value(var d): val = d; break;
            case PointerValue(var p): val = p; break;
            case StringValue(var s): val = double.Parse(s); break;
            case BoolValue(var b): if (b) { val = 1; } else { val = 0; } break;
        }
        return val;
    }

    float VariableToFloat(VariableValue value)
    {
        float val = 0.0f;
        switch (value)
        {
            case I32Value(var i): val = i; break;
            case I64Value(var l): val = l; break;
            case F32Value(var f): val = f; break;
            case F64Value(var d): val = (float)d; break;
        }
        return val;
    }

    VariableValue TypeAjust(string type, VariableValue value)
    {
        double val = 0;
        string s_val = "";
        bool b_val = false;
        VecPtrAndSize vps_val = new VecPtrAndSize { ptr = 0, len = 0, capacity = 0 };
        bool is_NaV = true;
        if (type == "String") { value = To_String(value); }
        switch (value)
        {
            case F64Value(var d): val = d; break;
            case F32Value(var f): val = f; break;
            case I64Value(var l): val = l; break;
            case I32Value(var i): val = i; break;
            case StringValue(var s): s_val = s; if (type == "f64" || type == "f32" || type == "i64" || type == "i32") { val = double.Parse(s); } break;
            case BoolValue(var b): b_val = b; if (type == "f64" || type == "f32" || type == "i64" || type == "i32") { if (b) { val = 1; } else { val = 0; } } break;
            case PointerValue(var p): val = p; break;
            case VecElements(var ve): if (type == "iter") { return new IteratorValue(new Iterator { element = new VecElements(ve), pos = 0, size = ve.Count }); } break;
            case VecValue(var vv): vps_val = vv; is_NaV = false; break;
            case IteratorValue(var iter): if (type == "vec") { return iter.element; } break;
        }
        switch (type)
        {
            case "i32": return new I32Value((int)val);
            case "i64": return new I64Value((long)val);
            case "f32": return new F32Value((float)val);
            case "f64": return new F64Value(val);
            case "String": return new StringValue(s_val);
            case "bool": return new BoolValue(b_val);
            case "ptr": return new PointerValue((int)val);
            case "vec": if (!is_NaV) { return new VecValue(vps_val); } else { return value; }
            case "gob": return value;
            default: return value;
        }
    }
    string TypeReturn(VariableValue value)
    {
        string ret = "null";
        switch (value)
        {
            case I32Value(var i): ret = "i32"; break;
            case I64Value(var l): ret = "i64"; break;
            case F32Value(var f): ret = "f32"; break;
            case F64Value(var d): ret = "f64"; break;
            case StringValue(var s): ret = "String"; break;
            case BoolValue(var b): ret = "bool"; break;
            case PointerValue(var p): ret = "ptr"; break;
            case VecValue(var v): ret = "vec"; break;
            case VecElements(var ve): ret = "vece"; break;
            case IteratorValue(var iter): ret = "iter"; break;
        }
        return ret;
    }
    bool VariableToBool(VariableValue value)
    {
        bool val = false;
        switch (value)
        {
            case BoolValue(var b): val = b; break;
        }
        return val;
    }
    string VariableToString(VariableValue value)
    {
        string val = "";
        switch (value)
        {
            case StringValue(var b): val = b; break;
        }
        return val;
    }
    VariableValue To_String(VariableValue value)
    {
        string s_val = "";
        switch (value)
        {
            case F64Value(var d): s_val = d.ToString(); break;
            case F32Value(var f): s_val = f.ToString(); break;
            case I64Value(var l): s_val = l.ToString(); break;
            case I32Value(var i): s_val = i.ToString(); break;
            case StringValue(var s): s_val = s; break;
            case BoolValue(var b): s_val = b.ToString(); break;
            case PointerValue(var p): s_val = p.ToString(); break;
            case NullableValue(var n): if (n == "") { s_val = "Null"; } else { s_val = n; } break;
        }
        return new StringValue(s_val);
    }
    int alloc(int size)
    {
        int start = vmemory.emptyArea[0].start;
        int pre_size = vmemory.emptyArea[0].size;
        int index = 0;
        for (int i = 0; i < vmemory.emptyArea.Count; i++)
        {
            if (vmemory.emptyArea[i].size >= size)
            {
                start = vmemory.emptyArea[i].start;
                pre_size = vmemory.emptyArea[i].size;
                index = i;
                break;
            }
        }
        vmemory.emptyArea.RemoveAt(index);
        vmemory.emptyArea.Add(new EmptyArea { start = start + size, size = pre_size - size });

        return start;
    }
    void free(int ptr, int size)
    {
        for (int i = 0; i < size; i++)
        {
            vmemory.memory[ptr + i] = new NullableValue("");
        }
        vmemory.emptyArea.Add(new EmptyArea { start = ptr, size = size });
        connectEnptyMem();
    }

    void connectEnptyMem()
    {
        vmemory.emptyArea.Sort((a, b) => a.start.CompareTo(b.start));
        List<EmptyArea> NewEmpty = new List<EmptyArea>(vmemory.emptyArea.Count);
        foreach (EmptyArea emptyarea in vmemory.emptyArea)
        {
            if (NewEmpty.Count > 0 && NewEmpty[NewEmpty.Count - 1].end() == emptyarea.start - 1)
            {
                NewEmpty[NewEmpty.Count - 1].size += emptyarea.size;
            }
            else
            {
                NewEmpty.Add(emptyarea);
            }
        }
        vmemory.emptyArea = NewEmpty;
    }
}

