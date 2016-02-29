﻿using Pchp.Syntax;
using Pchp.Syntax.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pchp.CodeAnalysis.Symbols;
using System.Diagnostics;
using System.Collections.Immutable;
using Pchp.CodeAnalysis.Semantics;

namespace Pchp.CodeAnalysis.FlowAnalysis.Visitors
{
    /// <summary>
    /// Walks through local variables.
    /// </summary>
    internal class LocalsWalker : TreeVisitor
    {
        protected class VisitLocalArgs : EventArgs
        {
            public VariableName Name;
            public VariableKind Kind;
            public Expression Initializer;

            public VisitLocalArgs(VariableName name, VariableKind kind, Expression initializer)
            {
                this.Name = name;
                this.Kind = kind;
                this.Initializer = initializer;
            }
        }

        protected event EventHandler<VisitLocalArgs> VisitLocal;

        protected void OnLocal(VisitLocalArgs args)
        {
            Debug.Assert(args != null);

            var e = this.VisitLocal;
            if (e != null)
                e(this, args);
        }

        VariableKind _statementContext;
        LangElement _routine;

        public LocalsWalker(LangElement routine)
        {
            _statementContext = VariableKind.LocalVariable;
            _routine = routine;
        }

        public void VisitRoutine()
        {
            VisitElement(_routine);
        }

        #region AddVar

        private void AddVar(VariableName name, Syntax.Text.Span span)
        {
            AddVar(name, span, _statementContext, null);
        }

        private void AddVar(VariableName name, Syntax.Text.Span span, VariableKind kind, Expression initializer = null)
        {
            if (name.IsThisVariableName)
                kind = VariableKind.ThisParameter;

            if (kind != VariableKind.GlobalVariable && kind != VariableKind.ThisParameter && kind != VariableKind.Parameter && kind != VariableKind.ReturnVariable    // just avoid checking IsAutoGlobal if not necessary
                && name.IsAutoGlobal)
                kind = VariableKind.GlobalVariable;

            OnLocal(new VisitLocalArgs(name, kind, initializer));
        }

        #endregion

        #region Declarations

        public override void VisitFunctionDecl(FunctionDecl x)
        {
            if (x == _routine)
            {
                _statementContext = VariableKind.LocalVariable;
                base.VisitFunctionDecl(x);
            }
        }

        public override void VisitMethodDecl(MethodDecl x)
        {
            if (x == _routine)
            {
                _statementContext = VariableKind.LocalVariable;
                base.VisitMethodDecl(x);
            }
        }

        public override void VisitTypeDecl(TypeDecl x)
        {
            // nope
        }

        public override void VisitLambdaFunctionExpr(LambdaFunctionExpr x)
        {
            if (x == _routine)
            {
                _statementContext = VariableKind.LocalVariable;

                // use params
                if (x.UseParams != null)
                    foreach (var u in x.UseParams)
                        AddVar(u.Name, u.Span, VariableKind.Parameter);

                // params
                x.Signature.FormalParams.ForEach(VisitFormalParam);

                // body
                x.Body.ForEach(VisitElement);
            }
        }

        public override void VisitNamespaceDecl(NamespaceDecl x)
        {
            _statementContext = VariableKind.GlobalVariable;
            base.VisitNamespaceDecl(x);
        }

        public override void VisitGlobalCode(GlobalCode x)
        {
            _statementContext = VariableKind.GlobalVariable;
            base.VisitGlobalCode(x);
        }

        #endregion

        public override void VisitDirectVarUse(DirectVarUse x)
        {
            if (x.IsMemberOf == null)
            {
                AddVar(x.VarName, x.Span);
            }

            base.VisitDirectVarUse(x);
        }

        public override void VisitForeachStmt(ForeachStmt x)
        {
            var valuevar = x.ValueVariable; // VariableUse or ListEx
            var keyvar = x.KeyVariable;     // VariableUse or null

            if (valuevar.Variable != null)
                valuevar.Variable.VisitMe(this);
            else if (valuevar.List != null)
                valuevar.List.VisitMe(this);

            if (keyvar != null && keyvar.Variable != null)
                keyvar.Variable.VisitMe(this);

            //
            base.VisitForeachStmt(x);
        }

        public override void VisitGlobalStmt(GlobalStmt x)
        {
            var prevCtx = _statementContext;
            _statementContext = VariableKind.GlobalVariable;
            base.VisitGlobalStmt(x);
            _statementContext = prevCtx;
        }

        public override void VisitStaticStmt(StaticStmt x)
        {
            foreach (var st in x.StVarList)
            {
                VisitElement(st.Initializer);
                Debug.Assert(st.Variable.IsMemberOf == null);
                AddVar(st.Variable.VarName, st.Span, VariableKind.StaticVariable, st.Initializer);
            }
        }

        public override void VisitFormalParam(FormalParam x)
        {
            AddVar(x.Name, x.Span, VariableKind.Parameter);
        }

        public override void VisitJumpStmt(JumpStmt x)
        {
            if (x.Type == JumpStmt.Types.Return && x.Expression != null)
            {
                AddVar(new VariableName(SourceReturnSymbol.SpecialName), x.Span, VariableKind.ReturnVariable);
            }

            base.VisitJumpStmt(x);
        }
    }

    internal class LocalsCollector : LocalsWalker
    {
        readonly SourceRoutineSymbol _routine;
        readonly List<BoundVariable> _locals = new List<BoundVariable>();
        readonly HashSet<VariableName>/*!*/_visited = new HashSet<VariableName>();
        
        private LocalsCollector(SourceRoutineSymbol routine)
            :base(routine.Syntax)
        {
            _routine = routine;

            this.VisitLocal += this.HandleLocal;
        }

        public static ImmutableArray<BoundVariable> GetLocals(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            var visitor = new LocalsCollector(routine);
            visitor.VisitRoutine();
            return visitor._locals.ToImmutableArray();
        }

        void HandleLocal(object sender, VisitLocalArgs e)
        {
            Debug.Assert(sender == this);

            if (_visited.Add(e.Name))
            {
                switch (e.Kind)
                {
                    case VariableKind.ThisParameter:
                        _locals.Add(new BoundParameter(new ThisParameterSymbol(_routine)));
                        break;
                    case VariableKind.Parameter:
                        _locals.Add(new BoundParameter((SourceParameterSymbol)_routine.Parameters.First(p => p.Name == e.Name.Value)));
                        break;
                    case VariableKind.LocalVariable:
                        _locals.Add(new BoundLocal(new SourceLocalSymbol(_routine, e.Name.Value, e.Kind)));
                        break;
                    case VariableKind.StaticVariable:
                        _locals.Add(new BoundStaticLocal(
                            new SourceLocalSymbol(_routine, e.Name.Value, e.Kind),
                            (e.Initializer != null) ? SemanticsBinder.BindExpression(e.Initializer) : null));
                        break;
                    case VariableKind.ReturnVariable:   // for analysis purposes
                        _locals.Add(new BoundLocal(new SourceReturnSymbol(_routine)));
                        break;
                    case VariableKind.GlobalVariable:
                        _locals.Add(new BoundGlobalVariable(e.Name.Value));
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                // TODO: check kind matches with previous declaration
            }
        }
    }
}