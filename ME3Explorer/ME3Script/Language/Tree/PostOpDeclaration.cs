﻿using ME3Script.Analysis.Visitors;
using ME3Script.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ME3Explorer.Unreal.BinaryConverters;

namespace ME3Script.Language.Tree
{
    public class PostOpDeclaration : OperatorDeclaration
    {
        public FunctionParameter Operand;

        public PostOpDeclaration(string keyword,
            bool delim, CodeBody body, VariableType returnType,
            FunctionParameter operand, FunctionFlags flags,
            SourcePosition start, SourcePosition end)
            : base(ASTNodeType.PostfixOperator, keyword, delim, body, returnType, flags, start, end)
        {
            Operand = operand;
        }

        public override bool AcceptVisitor(IASTVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public bool IdenticalSignature(PostOpDeclaration other)
        {
            return base.IdenticalSignature(other)
                && this.Operand.VarType.Name.ToLower() == other.Operand.VarType.Name.ToLower();
        }
        public override IEnumerable<ASTNode> ChildNodes
        {
            get
            {
                yield return Operand;
            }
        }
    }
}