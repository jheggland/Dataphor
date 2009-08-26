/*
	Dataphor
	© Copyright 2000-2008 Alphora
	This file is licensed under a modified BSD-license which can be found here: http://dataphor.org/dataphor_license.txt
*/

using System;
using System.Collections;

namespace Alphora.Dataphor.DAE.Runtime.Data
{
	using Alphora.Dataphor.DAE.Language.D4;
	using Alphora.Dataphor.DAE.Compiling;
	using Alphora.Dataphor.DAE.Streams;
	using Alphora.Dataphor.DAE.Runtime;
	using Alphora.Dataphor.DAE.Runtime.Data;
	using Alphora.Dataphor.DAE.Runtime.Instructions;
	using Alphora.Dataphor.DAE.Device.Memory;
	using Schema = Alphora.Dataphor.DAE.Schema;

    public abstract class RestrictTable : Table
    {
        public RestrictTable(RestrictNode ANode, Program AProgram) : base(ANode, AProgram){}
        
        public new RestrictNode Node { get { return (RestrictNode)FNode; } }
		
		protected Table FSourceTable;
		protected Row FSourceRow;
        
        protected override void InternalOpen()
        {
			FSourceTable = (Table)Node.Nodes[0].Execute(Program);
			try
			{
				FSourceRow = new Row(Manager, FSourceTable.DataType.RowType);
			}
			catch
			{
				FSourceTable.Dispose();
				throw;
			}
        }
        
        protected override void InternalClose()
        {
            if (FSourceRow != null)
            {
				FSourceRow.Dispose();
				FSourceRow = null;
            }

			if (FSourceTable != null)
			{
				FSourceTable.Dispose();
				FSourceTable = null;
			}
        }
        
        protected override void InternalSelect(Row ARow)
        {
            FSourceTable.Select(ARow);
        }
    }
    
    public class FilterTable : RestrictTable
    {
		public FilterTable(RestrictNode ANode, Program AProgram) : base(ANode, AProgram) {}
		
		protected bool FBOF;
		
		protected override void InternalOpen()
		{
			base.InternalOpen();
			FBOF = true;
		}

        protected override void InternalReset()
        {
            FSourceTable.Reset();
            FBOF = true;
        }
        
        protected override bool InternalNext()
        {
            while (FSourceTable.Next())
            {
                FSourceTable.Select(FSourceRow);
                Program.Stack.Push(FSourceRow);
                try
                {
					object LValue = Node.Nodes[1].Execute(Program);
					if ((LValue != null) && (bool)LValue)
					{
						FBOF = false;
					    return true;
					}
				}
				finally
				{
					Program.Stack.Pop();
				}
            }
            return false;
        }
        
        protected override bool InternalBOF()
        {
			return FBOF;
        }
        
        protected override bool InternalEOF()
        {
			if (FBOF)
			{
				InternalNext();
				if (FSourceTable.EOF())
					return true;
				else
				{
					if (FSourceTable.Supports(CursorCapability.BackwardsNavigable))
						FSourceTable.First();
					else
						FSourceTable.Reset();
					FBOF = true;
					return false;
				}
			}
			return FSourceTable.EOF();
        }
    }
    
    public class ScanKey : System.Object
    {
		public ScanKey(object AArgument, bool AIsExclusive) : base()
		{
			Argument = AArgument;
			IsExclusive = AIsExclusive;
		}
		
		public object Argument;
		public bool IsExclusive;
    }
    
    public class ScanTable : RestrictTable
    {
		public ScanTable(RestrictNode ANode, Program AProgram) : base(ANode, AProgram) {}
		
		protected bool FBOF;
		protected bool FEOF;

		protected Row FFirstKey;
		protected Row FLastKey;

		protected bool FIsFirstKeyExclusive;
		protected bool FIsLastKeyExclusive;
		protected bool FIsContradiction;
		public ScanKey[] FFirstKeys;
		public ScanKey[] FLastKeys;
		
		public ScanDirection Direction; // ??
		
		protected void ResolveKeys()
		{
			if (Node.OpenConditionsUseFirstKey)
			{
				FFirstKeys = new ScanKey[Node.ClosedConditions.Count + Node.OpenConditions.Count];
				FLastKeys = new ScanKey[Node.ClosedConditions.Count];
			}
			else
			{
				FFirstKeys = new ScanKey[Node.ClosedConditions.Count];
				FLastKeys = new ScanKey[Node.ClosedConditions.Count + Node.OpenConditions.Count];
			}
		
			Program.Stack.Push(null); // var Dummy : Scalar?
			try
			{
				for (int LIndex = 0; LIndex < Node.Order.Columns.Count; LIndex++)
				{
					ColumnConditions LCondition;
					if (LIndex < Node.ClosedConditions.Count)
					{
						LCondition = Node.ClosedConditions[Node.Order.Columns[LIndex].Column];
						
						// A condition in which there is only one condition that is equal, or either both conditions are equal, or both conditions are not equal, and comparison operators are opposite
						if (LCondition.Count == 1)
						{
							FFirstKeys[LIndex] = new ScanKey(LCondition[0].Argument.Execute(Program), false);
							FLastKeys[LIndex] = new ScanKey(FFirstKeys[LIndex].Argument, false);
						}
						else
						{
							// If both conditions are equal, and the arguments are not equal, this is a contradiction
							if (LCondition[0].Instruction == Instructions.Equal)
							{
								FFirstKeys[LIndex] = new ScanKey(LCondition[0].Argument.Execute(Program), false);
								FLastKeys[LIndex] = new ScanKey(LCondition[1].Argument.Execute(Program), false);
								if (!(bool)Compiler.EmitEqualNode(Program.Plan, new ValueNode(LCondition[0].Argument.DataType, FFirstKeys[LIndex].Argument), new ValueNode(LCondition[1].Argument.DataType, FLastKeys[LIndex].Argument)).Execute(Program))
								{
									FIsContradiction = true;
									return;
								}
							}
							else
							{
								// Instructions are opposite (one is a less, the other is a greater)
								// If both conditions are not equal, and the arguments are not consistent, this is a contradiction
								ColumnCondition LLessCondition;
								ColumnCondition LGreaterCondition;
								bool LIsFirstLess;
								if (Instructions.IsLessInstruction(LCondition[0].Instruction))
								{
									LIsFirstLess = true;
									LLessCondition = LCondition[0];
									LGreaterCondition = LCondition[1];
								}
								else
								{
									LIsFirstLess = false;
									LLessCondition = LCondition[1];
									LGreaterCondition = LCondition[0];
								}
								
								FFirstKeys[LIndex] = new ScanKey(LGreaterCondition.Argument.Execute(Program), Instructions.IsExclusiveInstruction(Node.ClosedConditions[LIndex][LIsFirstLess ? 1 : 0].Instruction));
								FLastKeys[LIndex] = new ScanKey(LLessCondition.Argument.Execute(Program), Instructions.IsExclusiveInstruction(Node.ClosedConditions[LIndex][LIsFirstLess ? 0 : 1].Instruction));
								
								int LCompareValue = (int)Compiler.EmitBinaryNode(Program.Plan, new ValueNode(LLessCondition.Argument.DataType, FLastKeys[LIndex].Argument), Instructions.Compare, new ValueNode(LGreaterCondition.Argument.DataType, FFirstKeys[LIndex].Argument)).Execute(Program);
								if ((LLessCondition.Instruction == Instructions.Less) && (LGreaterCondition.Instruction == Instructions.Greater))
								{
									if (LCompareValue <= 0)
									{
										FIsContradiction = true;
										return;
									}
								}
								else
								{
									if (LCompareValue < 0)
									{
										FIsContradiction = true;
										return;
									}
								}
							}
						}

						if ((FFirstKeys[LIndex].Argument == null) || (FLastKeys[LIndex].Argument == null))
						{
							FIsContradiction = true;
							return;
						}
					}
					else if (LIndex < (Node.ClosedConditions.Count + Node.OpenConditions.Count))
					{
						LCondition = Node.OpenConditions[Node.Order.Columns[LIndex].Column];

						// A condition in which there is at least one non-equal comparison
						if (LCondition.Count == 1)
							if (Instructions.IsLessInstruction(LCondition[0].Instruction))
							{
								FLastKeys[LIndex] = new ScanKey(LCondition[0].Argument.Execute(Program), Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
								if ((FLastKeys[LIndex].Argument == null))
								{
									FIsContradiction = true;
									return;
								}
							}
							else
							{
								FFirstKeys[LIndex] = new ScanKey(LCondition[0].Argument.Execute(Program), Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
								if ((FFirstKeys[LIndex].Argument == null))
								{
									FIsContradiction = true;
									return;
								}
							}
						else
						{
							if ((LCondition[0].Instruction == Instructions.Equal) || (LCondition[1].Instruction == Instructions.Equal))
							{
								// one is equal, one is a relative operator
								ColumnCondition LEqualCondition;
								ColumnCondition LCompareCondition;
								if (LCondition[0].Instruction == Instructions.Equal)
								{
									LEqualCondition = LCondition[0];
									LCompareCondition = LCondition[1];
								}
								else
								{
									LEqualCondition = LCondition[1];
									LCompareCondition = LCondition[0];
								}
								
								object LEqualVar = LEqualCondition.Argument.Execute(Program);
								object LCompareVar = LCompareCondition.Argument.Execute(Program);
								
								if (Instructions.IsLessInstruction(LCompareCondition.Instruction))
								{
									FLastKeys[LIndex] = new ScanKey(LCompareVar, Instructions.IsExclusiveInstruction(LCompareCondition.Instruction));
									if ((FLastKeys[LIndex].Argument == null))
									{
										FIsContradiction = true;
										return;
									}
								}
								else
								{
									FFirstKeys[LIndex] = new ScanKey(LCompareVar, Instructions.IsExclusiveInstruction(LCompareCondition.Instruction));
									if ((FFirstKeys[LIndex].Argument == null))
									{
										FIsContradiction = true;
										return;
									}
								}
									
								if (!(bool)Compiler.EmitBinaryNode(Program.Plan, new ValueNode(LEqualCondition.Argument.DataType, LEqualVar), LCompareCondition.Instruction, new ValueNode(LCompareCondition.Argument.DataType, LCompareVar)).Execute(Program))
								{
									FIsContradiction = true;
									return;
								}
							}
							else 
							{
								object LZeroVar = LCondition[0].Argument.Execute(Program);
								object LOneVar = LCondition[1].Argument.Execute(Program);
								int LCompareValue = (int)Compiler.EmitBinaryNode(Program.Plan, new ValueNode(LCondition[0].Argument.DataType, LZeroVar), Instructions.Compare, new ValueNode(LCondition[1].Argument.DataType, LOneVar)).Execute(Program);
								if (Instructions.IsLessInstruction(LCondition[0].Instruction))
								{
									// both are less instructions
									if (LCompareValue == 0)
										if (LCondition[0].Instruction == Instructions.Less)
											FLastKeys[LIndex] = new ScanKey(LZeroVar, Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
										else
											FLastKeys[LIndex] = new ScanKey(LOneVar, Instructions.IsExclusiveInstruction(LCondition[1].Instruction));
									else if (LCompareValue > 0)
										FLastKeys[LIndex] = new ScanKey(LZeroVar, Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
									else
										FLastKeys[LIndex] = new ScanKey(LOneVar, Instructions.IsExclusiveInstruction(LCondition[1].Instruction));

									if ((FLastKeys[LIndex].Argument == null))
									{
										FIsContradiction = true;
										return;
									}
								}
								else
								{
									// both are greater instructions
									if (LCompareValue == 0)
										if (LCondition[0].Instruction == Instructions.Greater)
											FFirstKeys[LIndex] = new ScanKey(LZeroVar, Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
										else
											FFirstKeys[LIndex] = new ScanKey(LOneVar, Instructions.IsExclusiveInstruction(LCondition[1].Instruction));
									else if (LCompareValue < 0)
										FFirstKeys[LIndex] = new ScanKey(LZeroVar, Instructions.IsExclusiveInstruction(LCondition[0].Instruction));
									else
										FFirstKeys[LIndex] = new ScanKey(LOneVar, Instructions.IsExclusiveInstruction(LCondition[1].Instruction));

									if ((FFirstKeys[LIndex].Argument == null))
									{
										FIsContradiction = true;
										return;
									}
								}
							}
						}
					}
				}
			}
			finally
			{
				Program.Stack.Pop();
			}
		}
		
		protected void CreateKeys()
		{
			Schema.RowType LRowType;
			
			if (FFirstKeys.Length > 0)
			{
				LRowType = new Schema.RowType();
				for (int LIndex = 0; LIndex < FFirstKeys.Length; LIndex++)
					LRowType.Columns.Add(Node.Order.Columns[LIndex].Column.Column.Copy());
				FFirstKey = new Row(Manager, LRowType);
				for (int LIndex = 0; LIndex < FFirstKeys.Length; LIndex++)
					if (FFirstKeys[LIndex].Argument != null)
						FFirstKey[LIndex] = FFirstKeys[LIndex].Argument;

				FIsFirstKeyExclusive = FFirstKeys[FFirstKeys.Length - 1].IsExclusive;
			}
			
			if (FLastKeys.Length > 0)
			{
				LRowType = new Schema.RowType();
				for (int LIndex = 0; LIndex < FLastKeys.Length; LIndex++)
					LRowType.Columns.Add(Node.Order.Columns[LIndex].Column.Column.Copy());
				FLastKey = new Row(Manager, LRowType);
				for (int LIndex = 0; LIndex < FLastKeys.Length; LIndex++)
					if (FLastKeys[LIndex].Argument != null)
						FLastKey[LIndex] = FLastKeys[LIndex].Argument;
				
				FIsLastKeyExclusive = FLastKeys[FLastKeys.Length - 1].IsExclusive;
			}
		}
		
		protected override void InternalOpen()
		{
			FIsContradiction = false;
			ResolveKeys();
			if (!FIsContradiction)
			{
				base.InternalOpen();
				CreateKeys();
				InternalFirst();
			}
			else
			{
				FBOF = true;
				FEOF = true;
			}
		}
		
        protected override void InternalClose()
        {
			if (FFirstKey != null)
			{
				FFirstKey.Dispose();
				FFirstKey = null;
			}
			
			if (FLastKey != null)
			{
				FLastKey.Dispose();
				FLastKey = null;
			}
			base.InternalClose();
        }
        
        protected override void InternalReset()
        {
			Close();
			Open();
        }
        
        protected override bool InternalRefresh(Row ARow)
        {
			InternalReset();
			if (ARow != null)
			{
				bool LResult = InternalFindKey(ARow, true);
				if (!LResult)
					InternalFindNearest(ARow);
				return LResult;
			}
			return false;
        }
        
        protected override bool InternalBOF()
        {
			return FBOF;
        }
        
        protected override bool InternalEOF()
        {
			return FEOF;
        }
        
        protected override void InternalFirst()
        {
			if (!FIsContradiction)
			{
				FBOF = true;
				if (Direction == ScanDirection.Forward)
				{
					if (FFirstKey != null)
					{
						FSourceTable.FindNearest(FFirstKey);
						
						if (FIsFirstKeyExclusive)
						{
							// navigate to the first key that is greater than the search key
							while (!FSourceTable.EOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FFirstKey) > 0)
									{
										FSourceTable.Prior();
										break;
									}
									else
										FSourceTable.Next();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
						else
						{
							if (FSourceTable.EOF())
								FSourceTable.Prior();

							while (!FSourceTable.BOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FFirstKey) < 0)
										break;
									else
										FSourceTable.Prior();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
					}
					else
						FSourceTable.First();
						
					FEOF = FSourceTable.EOF();
				}
				else
				{
					if (FLastKey != null)
					{
						if (!FSourceTable.FindKey(FFirstKey))
						{
							FSourceTable.FindNearest(FFirstKey);
							FSourceTable.Prior();
						}
						
						if (FIsFirstKeyExclusive)
						{
							while (!FSourceTable.BOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FFirstKey) > 0)
									{
										FSourceTable.Next();
										break;
									}
									else
										FSourceTable.Prior();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
						else
						{
							if (FSourceTable.BOF())
								FSourceTable.Next();

							while (!FSourceTable.EOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FFirstKey) < 0)
										break;
									else
										FSourceTable.Next();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
					}
					else
						FSourceTable.Last();
						
					FEOF = FSourceTable.BOF();
				}

				InternalNext();
				if (Direction == ScanDirection.Forward)
					FSourceTable.Prior();
				else
					FSourceTable.Next();
				FBOF = true;
			}
        }
        
        protected override void InternalLast()
        {
			if (!FIsContradiction)
			{
				FEOF = true;
				if (Direction == ScanDirection.Forward)
				{
					if (FLastKey != null)
					{
						if (!FSourceTable.FindKey(FLastKey))
						{
							FSourceTable.FindNearest(FLastKey);
							FSourceTable.Prior();
						}
						
						if (FIsLastKeyExclusive)
						{
							while (!FSourceTable.BOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FLastKey) < 0)
									{
										FSourceTable.Next();
										break;
									}
									else
										FSourceTable.Prior();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
						else
						{
							if (FSourceTable.BOF())
								FSourceTable.Next();

							while (!FSourceTable.EOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FLastKey) > 0)
										break;
									else
										FSourceTable.Next();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
					}
					else
						FSourceTable.Last();
					FBOF = FSourceTable.BOF();
				}
				else
				{
					if (FLastKey != null)
					{
						FSourceTable.FindNearest(FLastKey);
						
						if (FIsLastKeyExclusive)
						{
							while (!FSourceTable.EOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FLastKey) < 0)
									{
										FSourceTable.Prior();
										break;
									}
									else
										FSourceTable.Next();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
						else
						{
							if (FSourceTable.EOF())
								FSourceTable.Prior();
								
							while (!FSourceTable.BOF())
							{
								Row LCurrentKey = FSourceTable.GetKey();
								try
								{
									if (CompareKeys(LCurrentKey, FLastKey) > 0)
										break;
									else
										FSourceTable.Prior();
								}
								finally
								{
									LCurrentKey.Dispose();
								}
							}
						}
					}
					else
						FSourceTable.First();
					FBOF = FSourceTable.EOF();
				}
				InternalPrior();
				if (Direction == ScanDirection.Forward)
					FSourceTable.Next();
				else
					FSourceTable.Prior();
				FEOF = true;
			}
        }
        
        protected int CompareKeyValues(object AKeyValue1, object AKeyValue2, PlanNode ACompareNode)
        {
			Program.Stack.Push(AKeyValue1);
			Program.Stack.Push(AKeyValue2);
			int LResult = (int)ACompareNode.Execute(Program);
			Program.Stack.Pop();
			Program.Stack.Pop();
			return LResult;
        }
        
        protected int CompareKeys(Row AKey1, Row AKey2)
        {
			int LResult = 0;
			for (int LIndex = 0; LIndex < Node.Order.Columns.Count; LIndex++)
			{
				if ((LIndex >= AKey1.DataType.Columns.Count) || (LIndex >= AKey2.DataType.Columns.Count))
					return LResult;
				else if (AKey1.HasValue(LIndex))
					if (AKey2.HasValue(LIndex))
						LResult = CompareKeyValues(AKey1[LIndex], AKey2[LIndex], Node.Order.Columns[LIndex].Sort.CompareNode) * (Node.Order.Columns[LIndex].Ascending ? 1 : -1);
					else
						LResult = Node.Order.Columns[LIndex].Ascending ? 1 : -1;
				else
					if (AKey2.HasValue(LIndex))
						LResult = Node.Order.Columns[LIndex].Ascending ? -1 : 1;
					else
						LResult = 0;
				
				if (LResult != 0)
					return LResult;
			}
			return LResult;
        }
        
        protected bool IsGreater(Row AKey1, Row AKey2, bool AIsExclusive)
        {
			return CompareKeys(AKey1, AKey2) >= (AIsExclusive ? 0 : 1);
        }
        
		protected bool IsLess(Row AKey1, Row AKey2, bool AIsExclusive)
		{
			return CompareKeys(AKey1, AKey2) <= (AIsExclusive ? 0 : -1);
		}

        protected void CheckEOF()
        {
			if (!FEOF && (FLastKey != null) && !FSourceTable.BOF())
			{
				Row LKey = FSourceTable.GetKey();
				try
				{
					FEOF = IsGreater(LKey, FLastKey, FIsLastKeyExclusive);
				}
				finally
				{
					LKey.Dispose();
				}
			}
        }
        
        protected void CheckBOF()
        {
			if (!FBOF && (FFirstKey != null) && !FSourceTable.EOF())
			{
				Row LKey = FSourceTable.GetKey();
				try
				{
					FBOF = IsLess(LKey, FFirstKey, FIsFirstKeyExclusive);
				}
				finally
				{
					LKey.Dispose();
				}
			}
        }

        protected override bool InternalNext()
        {
			if (!FIsContradiction)
			{
				if (Direction == ScanDirection.Forward)
				{
					if (FSourceTable.Next())
					{
						FBOF = false;
						FEOF = false;
					}
					else
					{
						FBOF = FSourceTable.BOF();
						FEOF = true;
					}
				}
				else
				{
					if (FSourceTable.Prior())
					{
						FBOF = false;
						FEOF = false;
					}
					else
					{
						FBOF = FSourceTable.EOF();
						FEOF = true;
					}
				}
				CheckEOF();
				return !FEOF;
			}
			return false;
        }
        
        protected override bool InternalPrior()
        {
			if (!FIsContradiction)
			{
				if (Direction == ScanDirection.Forward)
				{
					if (FSourceTable.Prior())
					{
						FEOF = false;
						FBOF = false;
					}
					else
					{
						FEOF = FSourceTable.EOF();
						FBOF = true;
					}
				}
				else
				{
					if (FSourceTable.Next())
					{
						FEOF = false;
						FBOF = false;
					}
					else
					{
						FEOF = FSourceTable.BOF();
						FBOF = true;
					}
				}
				CheckBOF();
				return !FBOF;
			}
			return false;
        }
        
        protected override Row InternalGetKey()
        {
			return FSourceTable.GetKey();
        }

		protected override bool InternalFindKey(Row AKey, bool AForward)
        {
			if (!FIsContradiction)
			{
				Row LKey = EnsureKeyRow(AKey);
				try
				{
					if ((FFirstKey != null) && IsLess(LKey, FFirstKey, FIsFirstKeyExclusive))
						return false;
					else if ((FLastKey != null) && IsGreater(LKey, FLastKey, FIsLastKeyExclusive))
						return false;
					else
					{
						if (FSourceTable.FindKey(LKey, AForward))
						{
							FBOF = false;
							FEOF = false;
							return true;
						}
						else
							return false;
					}
				}
				finally
				{
					if (!Object.ReferenceEquals(LKey, AKey))
						LKey.Dispose();
				}
			}
			else
				return false;
        }
        
        protected override void InternalFindNearest(Row AKey)
        {
			if (!FIsContradiction)
			{
				Row LKey = EnsurePartialKeyRow(AKey);
				try
				{
					if (LKey != null)
					{
						if (Direction == ScanDirection.Forward)
						{
							if ((FFirstKey != null) && IsLess(LKey, FFirstKey, FIsFirstKeyExclusive))
							{
								InternalFirst();
								InternalNext();
							}
							else if ((FLastKey != null) && IsGreater(LKey, FLastKey, FIsLastKeyExclusive))
							{
								InternalLast();
								InternalPrior();
							}
							else
							{
								FSourceTable.FindNearest(LKey);
								FBOF = FSourceTable.BOF();
								FEOF = FSourceTable.EOF();
							}
						}
						else
						{
							if ((FFirstKey != null) && IsLess(LKey, FFirstKey, FIsFirstKeyExclusive))
							{
								InternalLast();
								InternalPrior();
							}
							else if ((FLastKey != null) && IsGreater(LKey, FLastKey, FIsLastKeyExclusive))
							{
								InternalFirst();
								InternalNext();
							}
							else
							{
								if (IsKeyRow(LKey) && FSourceTable.FindKey(LKey))
								{
									FBOF = false;
									FEOF = false;
								}
								else
								{
									FSourceTable.FindNearest(LKey);
									FSourceTable.Prior();
									FBOF = FSourceTable.BOF();
									FEOF = FSourceTable.EOF();
								}
							}				
						}
					}
				}
				finally
				{
					if (!Object.ReferenceEquals(AKey, LKey))
						LKey.Dispose();
				}
			}
        }
    }
    
    public class SeekTable : RestrictTable
    {
		public SeekTable(RestrictNode ANode, Program AProgram) : base(ANode, AProgram) {}
		
		protected bool FBOF;
		protected bool FEOF;
		protected bool FRowFound;
		protected Row FKeyRow;
		
		protected override void InternalOpen()
		{
			base.InternalOpen();
			int FKeyCount = 0;
			FKeyRow = new Row(Manager, new Schema.RowType(FSourceTable.Order.Columns));
			Program.Stack.Push(FKeyRow);
			try
			{
				object LVar;
				for (int LIndex = 0; LIndex < Node.FirstKeyNodes.Length; LIndex++)
				{
					LVar = Node.FirstKeyNodes[LIndex].Argument.Execute(Program);
					if ((LVar != null))
					{
						FKeyRow[LIndex] = LVar;
						FKeyCount++;
					}
				}
			}
			finally
			{
				Program.Stack.Pop();
			}
			FRowFound = (FKeyCount > 0) && FSourceTable.FindKey(FKeyRow);
			InternalFirst();
		}
		
		protected override void InternalClose()
		{
			if (FKeyRow != null)
			{
				FKeyRow.Dispose();
				FKeyRow = null;
			}				   
			base.InternalClose();
		}

		protected override bool InternalBOF()
		{
			return FBOF;
		}		
		
		protected override bool InternalEOF()
		{
			return FEOF;
		}
		
		protected override bool InternalNext()
		{
			if (FBOF)
				FBOF = !FRowFound;
			else
				FEOF = true;
			return !FEOF;
		}
		
		protected override bool InternalPrior()
		{
			if (FEOF)
				FEOF = !FRowFound;
			else
				FBOF = true;
			return !FBOF;
		}
		
		protected override void InternalFirst()
		{
			FBOF = true;
			FEOF = !FRowFound;
		}
		
		protected override void InternalLast()
		{
			FEOF = true;
			FBOF = !FRowFound;
		}
		
		protected override void InternalReset()
		{
			FSourceTable.Reset();
			FRowFound = FSourceTable.FindKey(FKeyRow);
			InternalFirst();
		}
    }
}