﻿using System;
using System.Collections.Generic;
using System.Linq;
using ME3Explorer;
using ME3Explorer.Unreal.BinaryConverters;
using ME3Script.Language.Tree;
using static ME3Explorer.Unreal.UnrealFlags;
using static ME3Script.Utilities.Keywords;

namespace ME3Script.Analysis.Visitors
{
    public class CodeBuilderVisitor : IASTVisitor
    {
        private readonly List<string> Lines;
        private int NestingLevel;
        private int ForcedAlignment;
        private bool ForceNoNewLines;
        private readonly Stack<int> ExpressionPrescedence;

        private const int NOPRESCEDENCE = int.MaxValue;

        public CodeBuilderVisitor()
        {
            Lines = new List<string>();
            NestingLevel = 0;
            ExpressionPrescedence = new Stack<int>();
            ExpressionPrescedence.Push(NOPRESCEDENCE);
        }

        public IList<string> GetCodeLines()
        {
            return Lines.AsReadOnly();
        }

        public string GetCodeString()
        {
            return string.Join("\n", Lines);
        }

        private void Write(string text)
        {
            if (ForceNoNewLines)
            {
                Append(text);
            }
            else
            {
                Lines.Add(new string(' ', ForcedAlignment + NestingLevel * 4) + text);
            }
        }

        private void Append(string text)
        {
            if (Lines.Count == 0)
                Lines.Add("");
            Lines[Lines.Count - 1] += text;
        }

        public bool VisitNode(Class node)
        {
            // class classname extends parentclass [within outerclass] [specifiers] ;
            Write($"{CLASS} {node.Name} {EXTENDS} {node.Parent.Name} ");
            if (node.OuterClass != null && node.OuterClass.Name != node.Parent.Name)
                Append($"{WITHIN} {node.OuterClass.Name} ");
            NestingLevel++;

            if (node.Interfaces.Any())
            {
                Write($"implements({string.Join(", ", node.Interfaces.Select(i => i.Name))})");
            }

            EClassFlags flags = node.Flags;
            if (flags.Has(EClassFlags.NativeOnly))
            {
                Write("nativeonly");
            }
            if (flags.Has(EClassFlags.NoExport))
            {
                Write("noexport");
            }
            if (flags.Has(EClassFlags.EditInlineNew))
            {
                Write("editinlinenew");
            }
            if (flags.Has(EClassFlags.Placeable))
            {
                Write("placeable");
            }
            if (flags.Has(EClassFlags.HideDropDown2))
            {
                Write("hidedropdown");
            }
            if (flags.Has(EClassFlags.NativeReplication))
            {
                Write("nativereplication");
            }
            if (flags.Has(EClassFlags.PerObjectConfig))
            {
                Write("perobjectconfig");
            }
            if (flags.Has(EClassFlags.Localized))
            {
                Write("localized");
            }
            if (flags.Has(EClassFlags.Abstract))
            {
                Write("abstract");
            }
            if (flags.Has(EClassFlags.Deprecated))
            {
                Write("deprecated");
            }
            if (flags.Has(EClassFlags.Transient))
            {
                Write("transient");
            }
            if (flags.Has(EClassFlags.Config))
            {
                Write($"config({node.ConfigName})");
            }
            if (flags.Has(EClassFlags.SafeReplace))
            {
                Write("safereplace");
            }
            if (flags.Has(EClassFlags.Hidden))
            {
                Write("hidden");
            }
            if (flags.Has(EClassFlags.CollapseCategories))
            {
                Write("collapsecategories");
            }

            NestingLevel--;
            Append(";");

            // print the rest of the class, according to the standard "anatomy of an unrealscript" article.
            if (node.TypeDeclarations.Count > 0)
            {
                Write("");
                Write("// Types");
                foreach (VariableType type in node.TypeDeclarations)
                    type.AcceptVisitor(this);
            }

            if (node.VariableDeclarations.Count > 0)
            {
                Write("");
                Write("// Variables");
                foreach (VariableDeclaration decl in node.VariableDeclarations.ToList())
                    decl.AcceptVisitor(this);
            }

            if (node.Operators.Count > 0)
            {
                Write("");
                Write("// Operators");
                foreach (OperatorDeclaration op in node.Operators)
                    op.AcceptVisitor(this);
            }

            if (node.Functions.Count > 0)
            {
                Write("");
                Write("// Functions");
                foreach (Function func in node.Functions)
                    func.AcceptVisitor(this);
            }

            if (node.States.Count > 0)
            {
                Write("");
                Write("// States");
                foreach (State state in node.States)
                    state.AcceptVisitor(this);
            }

            Write("");
            node.DefaultProperties?.AcceptVisitor(this);

            return true;
        }


        public bool VisitNode(VariableDeclaration node)
        {
            string type = "ERROR";
            if (node.Outer.Type == ASTNodeType.Class || node.Outer.Type == ASTNodeType.Struct)
            {
                type = VAR;
                if (!string.IsNullOrEmpty(node.Category))
                {
                    type += $"({node.Category})";
                }
            }
            else if (node.Outer.Type == ASTNodeType.Function)
                type = LOCAL;

            // var|local [specifiers] variabletype variablename[[staticarraysize]];
            Write($"{type} ");
            WritePropertyFlags(node.Flags);
            string staticarray = node.IsStaticArray ? $"[{node.Size}]" : "";
            node.VarType.AcceptVisitor(this);
            Append($" {node.Name}{staticarray};");
            
            return true;
        }

        public bool VisitNode(VariableType node)
        {
            Append(node.Name);
            return true;
        }

        public bool VisitNode(DynamicArrayType node)
        {
            Append($"{node.Name}<");
            node.ElementType.AcceptVisitor(this);
            Append(">");
            return true;
        }

        public bool VisitNode(DelegateType node)
        {
            Append($"{node.Name}<{node.FunctionName}>");
            return true;
        }

        public bool VisitNode(Struct node)
        {
            // struct [specifiers] structname [extends parentstruct] { \n contents \n };
            Write($"{STRUCT} ");
            
            var specs = new List<string>();
            ScriptStructFlags flags = node.Flags;
            if (flags.Has(ScriptStructFlags.Native))
            {
                specs.Add("native");
            }
            if (flags.Has(ScriptStructFlags.Export))
            {
                specs.Add("export");
            }
            if (flags.Has(ScriptStructFlags.Transient))
            {
                specs.Add("transient");
            }
            if (flags.Has(ScriptStructFlags.Immutable))
            {
                specs.Add("immutable");
            }
            else if (flags.Has(ScriptStructFlags.Atomic))
            {
                specs.Add("atomic");
            }
            if (flags.Has(ScriptStructFlags.ImmutableWhenCooked))
            {
                specs.Add("immutablewhencooked");
            }
            if (flags.Has(ScriptStructFlags.StrictConfig))
            {
                specs.Add("strictconfig");
            }

            if (specs.Any())
            {
                Append($"{string.Join(" ", specs)} ");
            }

            Append($"{node.Name} ");
            if (node.Parent != null)
                Append($"{EXTENDS} {node.Parent.Name} ");
            Append("{");
            NestingLevel++;

            foreach (VariableDeclaration member in node.Members)
                member.AcceptVisitor(this);

            NestingLevel--;
            Write("};");

            return true;
        }

        public bool VisitNode(Enumeration node)
        {
            // enum enumname { \n contents \n };
            Write($"{ENUM} {node.Name} {{");
            NestingLevel++;

            foreach (VariableIdentifier value in node.Values)
                Write($"{value.Name},");

            NestingLevel--;
            Write("};");

            return true;
        }

        public bool VisitNode(Const node)
        {
            Write($"{CONST} {node.Name} = {node.Value};");

            return true;
        }

        public bool VisitNode(Function node)
        {
            // [specifiers] function [returntype] functionname ( [parameter declarations] ) body_or_semicolon
            Write("");

            var specs = new List<string>();
            FunctionFlags flags = node.Flags;

            if (flags.Has(FunctionFlags.Private))
            {
                specs.Add("private");
            }
            if (flags.Has(FunctionFlags.Protected))
            {
                specs.Add("protected");
            }
            if (flags.Has(FunctionFlags.Public))
            {
                specs.Add("public");
            }
            if (flags.Has(FunctionFlags.Static))
            {
                specs.Add("static");
            }
            if (flags.Has(FunctionFlags.Final))
            {
                specs.Add("final");
            }
            if (flags.Has(FunctionFlags.Delegate))
            {
                specs.Add("delegate");
            }
            if (flags.Has(FunctionFlags.Event))
            {
                specs.Add("event");
            }
            if (flags.Has(FunctionFlags.PreOperator))
            {
                specs.Add("preoperator");
            }
            else if (flags.Has(FunctionFlags.Operator))
            {
                specs.Add("operator");
            }
            if (flags.Has(FunctionFlags.Native) && node.NativeIndex > 0)
            {
                specs.Add($"native({node.NativeIndex})");
            }
            else if (flags.Has(FunctionFlags.Native))
            {
                specs.Add("native");
            }
            if (flags.Has(FunctionFlags.Iterator))
            {
                specs.Add("iterator");
            }
            if (flags.Has(FunctionFlags.Singular))
            {
                specs.Add("singular");
            }
            if (flags.Has(FunctionFlags.Latent))
            {
                specs.Add("latent");
            }
            if (flags.Has(FunctionFlags.Exec))
            {
                specs.Add("exec");
            }
            if (flags.Has(FunctionFlags.NetReliable))
            {
                specs.Add("reliable");
            }
            else if (flags.Has(FunctionFlags.Net))
            {
                specs.Add("unreliable");
            }
            if (flags.Has(FunctionFlags.NetServer))
            {
                specs.Add("server");
            }
            if (flags.Has(FunctionFlags.NetClient))
            {
                specs.Add("client");
            }
            else if (flags.Has(FunctionFlags.Simulated))
            {
                specs.Add("simulated");
            }

            if (specs.Count > 0)
            {
                Append($"{string.Join(" ", specs)} ");
            }

            Append($"{FUNCTION} ");
            node.ReturnType?.AcceptVisitor(this);
            Append($" {node.Name}(");
            foreach (FunctionParameter p in node.Parameters)
            {
                p.AcceptVisitor(this);
                if (node.Parameters.IndexOf(p) != node.Parameters.Count - 1)
                    Append(", ");
            }
            Append(")");

            if (node.Body.Statements != null)
            {
                Write("{");
                NestingLevel++;
                foreach (VariableDeclaration v in node.Locals)
                    v.AcceptVisitor(this);
                node.Body.AcceptVisitor(this);
                NestingLevel--;
                Write("}");
            }
            else
                Append(";");

            return true;
        }

        public bool VisitNode(FunctionParameter node)
        {
            // [specifiers] parametertype parametername[[staticarraysize]]
            WritePropertyFlags(node.Flags);
            string staticarray = node.IsStaticArray ? "[" + node.Size + "]" : "";
            node.VarType.AcceptVisitor(this);
            Append($" {node.Name}{staticarray}");
            if (node.DefaultParameter != null)
            {
                Append(" = ");
                node.DefaultParameter.AcceptVisitor(this);
            }

            return true;
        }

        public bool VisitNode(State node)
        {
            // [specifiers] state statename [extends parentstruct] { \n contents \n };
            Write("");

            var specs = new List<string>();
            StateFlags flags = node.Flags;

            if (flags.Has(StateFlags.Simulated))
            {
                specs.Add("simulated");
            }
            if (flags.Has(StateFlags.Auto))
            {
                specs.Add("auto");
            }

            if (specs.Count > 0)
            {
                Append($"{string.Join(" ", specs)} ");
            }

            Append($"{STATE} {node.Name} ");
            if (node.Parent != null)
                Append($"{EXTENDS} {node.Parent.Name} ");
            Append("{");
            NestingLevel++;

            if (node.Ignores.Count > 0)
                Write($"{IGNORES} {string.Join(", ", node.Ignores.Select(x => x.Name))};");

            foreach (Function func in node.Functions)
                func.AcceptVisitor(this);


            if (node.Body.Statements.Count != 0)
            {
                Write("");
                Write("// State code");
                node.Body.AcceptVisitor(this);
            }

            NestingLevel--;
            Write("};");

            return true;
        }

        public bool VisitNode(OperatorDeclaration node)
        {
            // [specifiers] function [returntype] functionname ( [parameter declarations] ) body_or_semicolon
            Write("");
            
            if (node.Type == ASTNodeType.InfixOperator)
            {
                var inOp = (InOpDeclaration)node;
                Append($"{OPERATOR}({inOp.Precedence}) {(node.ReturnType != null ? node.ReturnType.Name + " " : "")}{node.OperatorKeyword}( ");
                inOp.LeftOperand.AcceptVisitor(this);
                Append(", ");
                inOp.RightOperand.AcceptVisitor(this);
            }
            else if (node.Type == ASTNodeType.PrefixOperator)
            {
                var preOp = (PreOpDeclaration)node;
                Append($"{PREOPERATOR} {(node.ReturnType != null ? node.ReturnType.Name + " " : "")}{node.OperatorKeyword}( ");
                preOp.Operand.AcceptVisitor(this);
            }
            else if (node.Type == ASTNodeType.PostfixOperator)
            {
                var postOp = (PostOpDeclaration)node;
                Append($"{POSTOPERATOR} {(node.ReturnType != null ? node.ReturnType.Name + " " : "")}{node.OperatorKeyword}( ");
                postOp.Operand.AcceptVisitor(this);
            }

            Append(" )");

            if (node.Body.Statements != null)
            {
                Write("{");
                NestingLevel++;
                foreach (VariableDeclaration v in node.Locals)
                    v.AcceptVisitor(this);
                node.Body.AcceptVisitor(this);
                NestingLevel--;
                Write("}");
            }
            else
                Append(";");

            return true;
        }

        public bool VisitNode(CodeBody node)
        {
            foreach (Statement s in node.Statements) //TODO: make this proper
            {
                s.AcceptVisitor(this);
            }

            return true; 
        }

        public bool VisitNode(DefaultPropertiesBlock node)
        {
            Write(DEFAULTPROPERTIES);
            Write("{");
            NestingLevel++;
            foreach (Statement s in node.Statements)
            {
                s.AcceptVisitor(this);
            }
            NestingLevel--;
            Write("}");

            return true;
        }

        public bool VisitNode(Subobject node)
        {
            Write($"Begin Object Class={node.Class} Name={node.Name}");
            NestingLevel++;
            foreach (Statement s in node.Statements)
            {
                s.AcceptVisitor(this);
            }
            NestingLevel--;
            Write("End Object");
            return true;
        }

        public bool VisitNode(DoUntilLoop node)
        { 
            // do { /n contents /n } until(condition);
            Write($"{DO} {{");
            NestingLevel++;

            node.Body.AcceptVisitor(this);
            NestingLevel--;

            Write($"}} {UNTIL} (");
            node.Condition.AcceptVisitor(this);
            Append(");");

            return true;
        }

        public bool VisitNode(ForLoop node)
        {
            // for (initstatement; loopcondition; updatestatement) { /n contents /n }
            Write($"{FOR} (");
            node.Init.AcceptVisitor(this);
            Append("; ");
            node.Condition.AcceptVisitor(this);
            Append("; ");
            node.Update.AcceptVisitor(this);
            Append(") {");

            NestingLevel++;
            node.Body.AcceptVisitor(this);
            NestingLevel--;
            Write("}");

            return true;
        }

        public bool VisitNode(ForEachLoop node)
        {
            // foreach IteratorFunction(parameters) { /n contents /n }
            Write($"{FOREACH} ");
            node.IteratorCall.AcceptVisitor(this);
            Append("{");

            NestingLevel++;
            node.Body.AcceptVisitor(this);
            NestingLevel--;
            Write("}");

            return true;
        }

        public bool VisitNode(WhileLoop node)
        {
            // while (condition) { /n contents /n }
            Write($"{WHILE} (");
            node.Condition.AcceptVisitor(this);
            Append(") {");

            NestingLevel++;
            node.Body.AcceptVisitor(this);
            NestingLevel--;
            Write("}");

            return true;
        }

        public bool VisitNode(SwitchStatement node)
        {
            // switch (expression) { /n contents /n }
            Write($"{SWITCH} (");
            node.Expression.AcceptVisitor(this);
            Append(") {");

            NestingLevel += 2;  // double-indent, only case/default are single-indented
            node.Body.AcceptVisitor(this);
            NestingLevel -= 2;
            Write("}");
            return true;
        }

        public bool VisitNode(CaseStatement node)
        {
            // case expression:
            NestingLevel--; // de-indent this line only
            Write($"{CASE} ");
            node.Value.AcceptVisitor(this);
            Append(":");
            NestingLevel++;
            return true;
        }

        public bool VisitNode(DefaultStatement node)
        {
            // default:
            NestingLevel--; // de-indent this line only
            Write($"{DEFAULT}:");
            NestingLevel++;
            return true;
        }

        public bool VisitNode(AssignStatement node)
        {
            // reference = expression;
            Write("");
            node.Target.AcceptVisitor(this);
            Append(" = ");
            node.Value.AcceptVisitor(this);

            return true;
        }

        public bool VisitNode(BreakStatement node)
        {
            // break;
            Write(BREAK);
            return true;
        }

        public bool VisitNode(ContinueStatement node)
        {
            // continue;
            Write(CONTINUE);
            return true;
        }

        public bool VisitNode(StopStatement node)
        {
            // stop;
            Write(STOP);
            return true;
        }
        
        public bool VisitNode(ReturnStatement node)
        {
            // return expression;
            Write(RETURN);
            if (node.Value != null)
            {
                Append(" ");
                node.Value.AcceptVisitor(this);
            }

            return true;
        }

        public bool VisitNode(ExpressionOnlyStatement node)
        {
            // expression;
            Write("");
            node.Value.AcceptVisitor(this);

            return true;
        }

        public bool VisitNode(IfStatement node)
        {
            // if (condition) { /n contents /n } [else...]
            VisitIf(node);

            return true;
        }

        private void VisitIf(IfStatement node, bool ifElse = false)
        {
            if (!ifElse)
                Write(""); // New line only if we're not chaining
            Append($"{IF} (");
            node.Condition.AcceptVisitor(this);
            Append(") {");

            NestingLevel++;
            node.Then.AcceptVisitor(this);
            NestingLevel--;
            Write("}");

            if (node.Else != null)
            {
                if (node.Else.Statements.Count == 1
                    && node.Else.Statements[0] is IfStatement)
                {
                    Append($" {ELSE} ");
                    VisitIf(node.Else.Statements[0] as IfStatement, true);
                }
                else
                {
                    Append($" {ELSE} {{");
                    NestingLevel++;
                    node.Else.AcceptVisitor(this);
                    NestingLevel--;
                    Write("}");
                }
            }
        }

        public bool VisitNode(ConditionalExpression node)
        {
            // condition ? then : else
            ExpressionPrescedence.Push(NOPRESCEDENCE);
            node.Condition.AcceptVisitor(this);
            Append(" ? ");
            node.TrueExpression.AcceptVisitor(this);
            Append(" : ");
            node.FalseExpression.AcceptVisitor(this);
            ExpressionPrescedence.Pop();

            return true;
        }

        public bool VisitNode(InOpReference node)
        {
            // [(] expression operatorkeyword expression [)]
            bool scopeNeeded = node.Operator.Precedence > ExpressionPrescedence.Peek();
            ExpressionPrescedence.Push(node.Operator.Precedence);

            if (scopeNeeded)
                Append("(");
            node.LeftOperand.AcceptVisitor(this);
            Append($" {node.Operator.OperatorKeyword} ");
            node.RightOperand.AcceptVisitor(this);
            if (scopeNeeded)
                Append(")");

            ExpressionPrescedence.Pop();
            return true;
        }

        public bool VisitNode(PreOpReference node)
        {
            ExpressionPrescedence.Push(NOPRESCEDENCE);
            // operatorkeywordExpression
            Append($"{node.Operator.OperatorKeyword}");
            node.Operand.AcceptVisitor(this);

            ExpressionPrescedence.Pop();
            return true;
        }

        public bool VisitNode(PostOpReference node)
        {
            ExpressionPrescedence.Push(NOPRESCEDENCE);
            // ExpressionOperatorkeyword
            node.Operand.AcceptVisitor(this);
            Append(node.Operator.OperatorKeyword);

            ExpressionPrescedence.Pop();
            return true;
        }

        public bool VisitNode(FunctionCall node)
        {
            ExpressionPrescedence.Push(NOPRESCEDENCE);
            // functionName( parameter1, parameter2.. )
            Append($"{node.Function.Name}(");
            foreach(Expression expr in node.Parameters)
            {
                expr.AcceptVisitor(this);
                if (node.Parameters.IndexOf(expr) != node.Parameters.Count - 1)
                    Append(", ");
            }
            Append(")");

            ExpressionPrescedence.Pop();
            return true;
        }

        public bool VisitNode(CastExpression node)
        {
            // type(expr)
            node.CastType.AcceptVisitor(this);
            Append("(");
            node.CastTarget.AcceptVisitor(this);
            Append(")");
            return true;
        }

        public bool VisitNode(ArraySymbolRef node)
        {
            ExpressionPrescedence.Push(NOPRESCEDENCE);
            // symbolname[expression]
            node.Array.AcceptVisitor(this);
            Append("[");
            node.Index.AcceptVisitor(this);
            Append("]");

            ExpressionPrescedence.Pop();
            return true;
        }

        public bool VisitNode(CompositeSymbolRef node)
        {
            // outersymbol.innersymbol
            node.OuterSymbol.AcceptVisitor(this);
            Append(".");
            node.InnerSymbol.AcceptVisitor(this);
            return true;
        }

        public bool VisitNode(SymbolReference node)
        {
            // symbolname
            Append(node.Name);
            return true;
        }

        public bool VisitNode(BooleanLiteral node)
        {
            // true|false
            Append(node.Value.ToString().ToLower());
            return true;
        }

        public bool VisitNode(FloatLiteral node)
        {
            // floatvalue
            Append($"{node.Value}");
            return true;
        }

        public bool VisitNode(IntegerLiteral node)
        {
            // integervalue
            Append($"{node.Value}");
            return true;
        }

        public bool VisitNode(NameLiteral node)
        {
            //unrealscript compliant version, but harder to parse
            //Append(node.Outer is StructLiteral ? "\"{0}\"" : "'{0}'", node.Value);
            Append($"'{node.Value}'");
            return true;
        }

        public bool VisitNode(StringLiteral node)
        {
            // "string"
            Append($"\"{node.Value}\"");
            return true;
        }

        public bool VisitNode(StringRefLiteral node)
        {
            Append($"${node.Value}");
            return true;
        }
        public bool VisitNode(StructLiteral node)
        {
            bool multiLine = !ForceNoNewLines && (node.Statements.Count > 5 || node.Statements.Any(stmnt => (stmnt as AssignStatement)?.Value is StructLiteral || (stmnt as AssignStatement)?.Value is DynamicArrayLiteral));

            bool oldForceNoNewLines = ForceNoNewLines;
            int oldForcedAlignment = ForcedAlignment;
            if (multiLine)
            {
                Append("{(");
                ForcedAlignment = Lines.Last().Length - NestingLevel * 4;
            }
            else
            {
                ForceNoNewLines = true;
                Append("(");
            }
            for (int i = 0; i < node.Statements.Count; i++)
            {
                if (i > 0)
                {
                    Append(", ");
                }
                node.Statements[i].AcceptVisitor(this);
            }

            if (multiLine)
            {
                ForcedAlignment -= 2;
                Write(")}");
                ForcedAlignment = oldForcedAlignment;
            }
            else
            {
                Append(")");
                ForceNoNewLines = oldForceNoNewLines;
            }
            return true;
        }

        public bool VisitNode(DynamicArrayLiteral node)
        {
            bool multiLine = !ForceNoNewLines && (node.Values.Any(expr => expr is StructLiteral || expr is DynamicArrayLiteral));

            bool oldForceNoNewLines = ForceNoNewLines;
            int oldForcedAlignment = ForcedAlignment;
            Append("(");
            if (multiLine)
            {
                ForcedAlignment = Lines.Last().Length - NestingLevel * 4;
            }
            else
            {
                ForceNoNewLines = true;
            }
            for (int i = 0; i < node.Values.Count; i++)
            {
                if (i > 0)
                {
                    Append(", ");
                    if (multiLine)
                    {
                        Write("");
                    }
                }
                node.Values[i].AcceptVisitor(this);
            }
            if (multiLine)
            {
                ForcedAlignment -= 1;
                Write(")");
                ForcedAlignment = oldForcedAlignment;
            }
            else
            {
                Append(")");
                ForceNoNewLines = oldForceNoNewLines;
            }
            return true;
        }

        public bool VisitNode(StateLabel node)
        {
            // Label
            var temp = NestingLevel;
            NestingLevel = 0;
            Write(node.Name + ":");
            NestingLevel = temp;

            return true;
        }

        private void WritePropertyFlags(EPropertyFlags flags)
        {
            var specs = new List<string>();
            if (flags.Has(EPropertyFlags.Const))
            {
                specs.Add("const");
            }

            if (flags.Has(EPropertyFlags.GlobalConfig))
            {
                specs.Add("globalconfig");
            }
            else if (flags.Has(EPropertyFlags.Config))
            {
                specs.Add("config");
            }

            if (flags.Has(EPropertyFlags.Localized))
            {
                specs.Add("localized");
            }

            //TODO: private, protected, and public are in ObjectFlags, not PropertyFlags 
            if (flags.Has(EPropertyFlags.ProtectedWrite))
            {
                specs.Add("protectedwrite");
            }

            if (flags.Has(EPropertyFlags.PrivateWrite))
            {
                specs.Add("privatewrite");
            }

            if (flags.Has(EPropertyFlags.EditConst))
            {
                specs.Add("editconst");
            }

            if (flags.Has(EPropertyFlags.EditHide))
            {
                specs.Add("edithide");
            }

            if (flags.Has(EPropertyFlags.EditTextBox))
            {
                specs.Add("edittextbox");
            }

            if (flags.Has(EPropertyFlags.Input))
            {
                specs.Add("input");
            }

            if (flags.Has(EPropertyFlags.Transient))
            {
                specs.Add("transient");
            }

            if (flags.Has(EPropertyFlags.Native))
            {
                specs.Add("native");
            }

            if (flags.Has(EPropertyFlags.NoExport))
            {
                specs.Add("noexport");
            }

            if (flags.Has(EPropertyFlags.DuplicateTransient))
            {
                specs.Add("duplicatetransient");
            }

            if (flags.Has(EPropertyFlags.NoImport))
            {
                specs.Add("noimport");
            }

            if (flags.Has(EPropertyFlags.OutParm))
            {
                specs.Add("out");
            }

            if (flags.Has(EPropertyFlags.ExportObject))
            {
                specs.Add("export");
            }

            if (flags.Has(EPropertyFlags.EditInlineUse))
            {
                specs.Add("editinlineuse");
            }
            else if (flags.Has(EPropertyFlags.EditInline))
            {
                specs.Add("editinline");
            }

            if (flags.Has(EPropertyFlags.NoClear))
            {
                specs.Add("noclear");
            }

            if (flags.Has(EPropertyFlags.EditFixedSize))
            {
                specs.Add("editfixedsize");
            }

            if (flags.Has(EPropertyFlags.RepNotify))
            {
                specs.Add("repnotify");
            }

            if (flags.Has(EPropertyFlags.RepRetry))
            {
                specs.Add("repretry");
            }

            if (flags.Has(EPropertyFlags.Interp))
            {
                specs.Add("interp");
            }

            if (flags.Has(EPropertyFlags.NonTransactional))
            {
                specs.Add("nontransactional");
            }

            if (flags.Has(EPropertyFlags.Deprecated))
            {
                specs.Add("deprecated");
            }

            if (flags.Has(EPropertyFlags.SkipParm))
            {
                specs.Add("skip");
            }

            if (flags.Has(EPropertyFlags.CoerceParm))
            {
                specs.Add("coerce");
            }

            if (flags.Has(EPropertyFlags.OptionalParm))
            {
                specs.Add("optional");
            }

            if (flags.Has(EPropertyFlags.AlwaysInit))
            {
                specs.Add("alwaysinit");
            }

            if (flags.Has(EPropertyFlags.EditInline) && flags.Has(EPropertyFlags.ExportObject))
            {
                specs.Add("instanced");
            }

            if (flags.Has(EPropertyFlags.DataBinding))
            {
                specs.Add("databinding");
            }

            if (flags.Has(EPropertyFlags.EditorOnly))
            {
                specs.Add("editoronly");
            }

            if (flags.Has(EPropertyFlags.NotForConsole))
            {
                specs.Add("notforconsole");
            }

            if (flags.Has(EPropertyFlags.Archetype))
            {
                specs.Add("archetype");
            }

            if (flags.Has(EPropertyFlags.SerializeText))
            {
                specs.Add("serializetext");
            }

            if (flags.Has(EPropertyFlags.CrossLevelActive))
            {
                specs.Add("crosslevelactive");
            }

            if (flags.Has(EPropertyFlags.CrossLevelPassive))
            {
                specs.Add("crosslevelpassive");
            }

            if (specs.Any())
            {
                Append($"{string.Join(" ", specs)} ");
            }
        }

        #region Unused

        public bool VisitNode(VariableIdentifier node)
        { throw new NotImplementedException(); }

        #endregion
    }
}