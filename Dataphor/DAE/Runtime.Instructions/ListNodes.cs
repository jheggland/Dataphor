/*
	Dataphor
	© Copyright 2000-2008 Alphora
	This file is licensed under a modified BSD-license which can be found here: http://dataphor.org/dataphor_license.txt
*/
#define NILPROPOGATION

using System;

namespace Alphora.Dataphor.DAE.Runtime.Instructions
{
	using Alphora.Dataphor.DAE.Compiling;
	using Alphora.Dataphor.DAE.Language;
	using Alphora.Dataphor.DAE.Language.D4;
	using Alphora.Dataphor.DAE.Runtime;
	using Alphora.Dataphor.DAE.Runtime.Data;
	using Schema = Alphora.Dataphor.DAE.Schema;

	public class ListNode : PlanNode
	{
		// ListType
		public Schema.ListType ListType { get { return (Schema.ListType)FDataType; } } 
		
		// DetermineDataType not used, ListNode.ListType is set by the compiler
		
		public override void DetermineCharacteristics(Plan APlan)
		{
			FIsLiteral = true;
			FIsFunctional = true;
			FIsDeterministic = true;
			FIsRepeatable = true;
			FIsNilable = false;
			for (int LIndex = 0; LIndex < NodeCount; LIndex++)
			{
				FIsLiteral = FIsLiteral && Nodes[LIndex].IsLiteral;
				FIsFunctional = FIsFunctional && Nodes[LIndex].IsFunctional;
				FIsDeterministic = FIsDeterministic && Nodes[LIndex].IsDeterministic;
				FIsRepeatable = FIsRepeatable && Nodes[LIndex].IsRepeatable;
			} 
		}
		
		// Evaluate
		public override object InternalExecute(Program AProgram)
		{
			ListValue LList = new ListValue(AProgram.ValueManager, ListType);
			for (int LIndex = 0; LIndex < NodeCount; LIndex++)
				LList.Add(Nodes[LIndex].Execute(AProgram));
			return LList;
		}
		
		public override Statement EmitStatement(EmitMode AMode)
		{
			ListSelectorExpression LExpression = new ListSelectorExpression();
			LExpression.TypeSpecifier = ListType.EmitSpecifier(AMode);
			for (int LIndex = 0; LIndex < NodeCount; LIndex++)
				LExpression.Expressions.Add(Nodes[LIndex].EmitStatement(AMode));
			LExpression.Modifiers = Modifiers;
			return LExpression;
		}
	}

	// operator iIndexer(const AList : list, const AIndex : integer) : generic
	public class IndexerNode : BinaryInstructionNode
	{
		public bool ByReference;
		
		public override void DetermineDataType(Plan APlan)
		{
			base.DetermineDataType(APlan);
			FDataType = ((Schema.ListType)Nodes[0].DataType).ElementType;
		}
		
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2)
		{
			#if NILPROPOGATION
			if (AArgument1 == null || AArgument2 == null)
				return null;
			#endif
			
			if (ByReference)
				return ((ListValue)AArgument1)[(int)AArgument2];

			return DataValue.CopyValue(AProgram.ValueManager, ((ListValue)AArgument1)[(int)AArgument2]);
		}

		public override Statement EmitStatement(EmitMode AMode)
		{
			D4IndexerExpression LExpression = new D4IndexerExpression();
			LExpression.Expression = (Expression)Nodes[0].EmitStatement(AMode);
			LExpression.Indexer = (Expression)Nodes[1].EmitStatement(AMode);
			LExpression.Modifiers = Modifiers;
			return LExpression;
		}
	}
	
	// operator Count(const AList : list) : integer;	
	public class ListCountNode : UnaryInstructionNode
	{
		public override object InternalExecute(Program AProgram, object AArgument1)
		{
			#if NILPROPOGATION
			if (AArgument1 == null)
				return null;
			#endif
			
			return ((ListValue)AArgument1).Count();
		}
	}
	
	// operator Clear(var AList : list);
	public class ListClearNode : UnaryInstructionNode
	{
		public override object InternalExecute(Program AProgram, object AArgument1)
		{
			((ListValue)AArgument1).Clear();
			return null;
		}
	}
	
	// operator Add(var AList : list, AValue : generic) : integer;
	public class ListAddNode : BinaryInstructionNode
	{
		public override void DetermineDataType(Plan APlan)
		{
			if (!Nodes[1].DataType.Is(((Schema.IListType)Nodes[0].DataType).ElementType))
			{
				ConversionContext LContext = Compiler.FindConversionPath(APlan, Nodes[1].DataType, ((Schema.IListType)Nodes[0].DataType).ElementType);
				Compiler.CheckConversionContext(APlan, LContext);
				Nodes[1] = Compiler.ConvertNode(APlan, Nodes[1], LContext);
			}

			Nodes[1] = Compiler.Upcast(APlan, Nodes[1], ((Schema.IListType)Nodes[0].DataType).ElementType);
			base.DetermineDataType(APlan);
		}
		
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2)
		{
			return ((ListValue)AArgument1).Add(AArgument2);
		}
	}
	
	// operator Insert(var AList : list, AValue : generic, AIndex : integer);
	public class ListInsertNode : TernaryInstructionNode
	{
		public override void DetermineDataType(Plan APlan)
		{
			if (!Nodes[1].DataType.Is(((Schema.IListType)Nodes[0].DataType).ElementType))
			{
				ConversionContext LContext = Compiler.FindConversionPath(APlan, Nodes[1].DataType, ((Schema.IListType)Nodes[0].DataType).ElementType);
				Compiler.CheckConversionContext(APlan, LContext);
				Nodes[1] = Compiler.ConvertNode(APlan, Nodes[1], LContext);
			}

			Nodes[1] = Compiler.Upcast(APlan, Nodes[1], ((Schema.IListType)Nodes[0].DataType).ElementType);
			base.DetermineDataType(APlan);
		}
		
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2, object AArgument3)
		{
			((ListValue)AArgument1).Insert((int)AArgument3, AArgument2);
			return null;
		}
	}
	
	// operator RemoveAt(var AList : list, const AIndex : integer);
	public class ListRemoveAtNode : BinaryInstructionNode
	{
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2)
		{
			((ListValue)AArgument1).RemoveAt((int)AArgument2);
			return null;
		}
	}
	
	public abstract class BaseListIndexOfNode : InstructionNode
	{
		public PlanNode FEqualNode; // The equality node used to compare each item in the list against AValue
		
		public override void DetermineDataType(Plan APlan)
		{
			DetermineModifiers(APlan);
			APlan.Symbols.Push(new Symbol("AValue", Nodes[1].DataType));
			try
			{
				APlan.Symbols.Push(new Symbol("ACompareValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
				try
				{
					FEqualNode = Compiler.CompileExpression(APlan, new BinaryExpression(new IdentifierExpression("AValue"), Instructions.Equal, new IdentifierExpression("ACompareValue")));
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}
		
		public override void InternalDetermineBinding(Plan APlan)
		{
			base.InternalDetermineBinding(APlan);
			APlan.Symbols.Push(new Symbol("AValue", Nodes[1].DataType));
			try
			{
				APlan.Symbols.Push(new Symbol("ACompareValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
				try
				{
					FEqualNode.DetermineBinding(APlan);
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}

		protected object InternalSearch(Program AProgram, ListValue AList, object AValue, int AStartIndex, int ALength, int AIncrementor)
		{
			if (ALength < 0)
				throw new RuntimeException(RuntimeException.Codes.InvalidLength, ErrorSeverity.Application);
			if (ALength == 0)
				return -1;

			int LStartIndex = Math.Max(Math.Min(AStartIndex, AList.Count() - 1), 0);
			int LEndIndex = Math.Max(Math.Min(AStartIndex + ((ALength - 1) * AIncrementor), AList.Count() - 1), 0);
			
			AProgram.Stack.Push(AValue);
			try
			{
				int LIndex = LStartIndex;
				while (((AIncrementor > 0) && (LIndex <= LEndIndex)) || ((AIncrementor < 0) && (LIndex >= LEndIndex)))
				{
					AProgram.Stack.Push(AList[LIndex]);
					try
					{
						object LValue = FEqualNode.Execute(AProgram);
						if ((LValue != null) && (bool)LValue)
							return LIndex;
					}
					finally
					{
						AProgram.Stack.Pop();
					}
					LIndex += AIncrementor;
				}
			}
			finally
			{
				AProgram.Stack.Pop();
			}

			return -1;
		}
	}

	// operator IndexOf(const AList : list, const AValue : generic);
	public class ListIndexOfNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null)
				return null;
			#endif
			
			ListValue LList = (ListValue)AArguments[0];
			return InternalSearch(AProgram, LList, AArguments[1], 0, LList.Count(), 1);
		}
	}

	// operator IndexOf(const AList : list, const AValue : generic, const AStartIndex : Integer);
	public class ListIndexOfStartNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null || AArguments[2] == null)
				return null;
			#endif
			
			ListValue LList = (ListValue)AArguments[0];
			return InternalSearch(AProgram, LList, AArguments[1], (int)AArguments[2], LList.Count(), 1);
		}
	}

	// operator IndexOf(const AList : list, const AValue : generic, const AStartIndex : Integer, const ALength : Integer);
	public class ListIndexOfStartLengthNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null || AArguments[2] == null || AArguments[3] == null)
				return null;
			#endif
			
			return InternalSearch(AProgram, (ListValue)AArguments[0], AArguments[1], (int)AArguments[2], (int)AArguments[3], 1);
		}
	}

	// operator LastIndexOf(const AList : list, const AValue : generic);
	public class ListLastIndexOfNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null)
				return null;
			#endif
			
			ListValue LList = (ListValue)AArguments[0];
			return InternalSearch(AProgram, LList, AArguments[1], LList.Count() - 1, LList.Count(), -1);
		}
	}

	// operator LastIndexOf(const AList : list, const AValue : generic, const AStartIndex : Integer);
	public class ListLastIndexOfStartNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null || AArguments[2] == null)
				return null;
			#endif
			
			ListValue LList = (ListValue)AArguments[0];
			return InternalSearch(AProgram, LList, AArguments[1], (int)AArguments[2], LList.Count(), -1);
		}
	}

	// operator LastIndexOf(const AList : list, const AValue : generic, const AStartIndex : Integer, const ALength : Integer);
	public class ListLastIndexOfStartLengthNode : BaseListIndexOfNode
	{
		public override object InternalExecute(Program AProgram, object[] AArguments)
		{
			#if NILPROPOGATION
			if (AArguments[0] == null || AArguments[1] == null || AArguments[2] == null || AArguments[3] == null)
				return null;
			#endif
			
			return InternalSearch(AProgram, (ListValue)AArguments[0], AArguments[1], (int)AArguments[2], (int)AArguments[3], -1);
		}
	}

	// operator Remove(var AList : list, const AValue : generic);
	public class ListRemoveNode : BinaryInstructionNode
	{
		public PlanNode FEqualNode; // The equality node used to compare each item in the list against AValue
		
		public override void DetermineDataType(Plan APlan)
		{
			base.DetermineDataType(APlan);
			APlan.Symbols.Push(new Symbol("AValue", Nodes[1].DataType));
			try
			{
				APlan.Symbols.Push(new Symbol("ACompareValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
				try
				{
					FEqualNode = Compiler.CompileExpression(APlan, new BinaryExpression(new IdentifierExpression("AValue"), Instructions.Equal, new IdentifierExpression("ACompareValue")));
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}
		
		public override void InternalDetermineBinding(Plan APlan)
		{
			base.InternalDetermineBinding(APlan);
			APlan.Symbols.Push(new Symbol("AValue", Nodes[1].DataType));
			try
			{
				APlan.Symbols.Push(new Symbol("ACompareValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
				try
				{
					FEqualNode.DetermineBinding(APlan);
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}
		
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2)
		{
			ListValue LList = (ListValue)AArgument1;
			int LListIndex = -1;
			AProgram.Stack.Push(AArgument2);
			try
			{
				for (int LIndex = 0; LIndex < LList.Count(); LIndex++)
				{
					AProgram.Stack.Push(LList[LIndex]);
					try
					{
						object LValue = FEqualNode.Execute(AProgram);
						if ((LValue != null) && (bool)LValue)
						{
							LListIndex = LIndex;
							break;
						}
					}
					finally
					{
						AProgram.Stack.Pop();
					}
				}
			}
			finally
			{
				AProgram.Stack.Pop();
			}

			((ListValue)AArgument1).RemoveAt(LListIndex);
			return null;
		}
	}
	
	// operator iEqual(const ALeftList : list, const ARightList : list) : Boolean;
	public class ListEqualNode : BinaryInstructionNode
	{
		public PlanNode FEqualNode; // The equality node used to compare successive values in the lists
		
		public override void DetermineDataType(Plan APlan)
		{
			DetermineModifiers(APlan);
			APlan.Symbols.Push(new Symbol("ALeftValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
			try
			{
				APlan.Symbols.Push(new Symbol("ARightValue", ((Schema.ListType)Nodes[1].DataType).ElementType));
				try
				{
					FEqualNode = Compiler.CompileExpression(APlan, new BinaryExpression(new IdentifierExpression("ALeftValue"), Instructions.Equal, new IdentifierExpression("ARightValue")));
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}
		
		public override void InternalDetermineBinding(Plan APlan)
		{
			base.InternalDetermineBinding(APlan);
			APlan.Symbols.Push(new Symbol("ALeftValue", ((Schema.ListType)Nodes[0].DataType).ElementType));
			try
			{
				APlan.Symbols.Push(new Symbol("ARightValue", ((Schema.ListType)Nodes[1].DataType).ElementType));
				try
				{
					FEqualNode.DetermineBinding(APlan);
				}
				finally
				{
					APlan.Symbols.Pop();
				}
			}
			finally
			{
				APlan.Symbols.Pop();
			}
		}
		
		public override object InternalExecute(Program AProgram, object AArgument1, object AArgument2)
		{
			ListValue LLeftList = (ListValue)AArgument1;
			ListValue LRightList = (ListValue)AArgument2;
			#if NILPROPOGATION
			if ((LLeftList == null) || (LRightList == null))
				return null;
			#endif
			
			bool LListsEqual = LLeftList.Count() == LRightList.Count();
			if (LListsEqual)
			{
				for (int LIndex = 0; LIndex < LLeftList.Count(); LIndex++)
				{
					AProgram.Stack.Push(LLeftList[LIndex]);
					try
					{
						AProgram.Stack.Push(LRightList[LIndex]);
						try
						{
							object LValue = FEqualNode.Execute(AProgram);
							#if NILPROPOGATION
							if ((LValue == null))
								return null;
							#endif

							LListsEqual = (bool)LValue;
							if (!LListsEqual)
								break;
						}
						finally
						{
							AProgram.Stack.Pop();
						}
					}
					finally
					{
						AProgram.Stack.Pop();
					}
				}
			}

			return LListsEqual;
		}
	}

	// operator ToTable(const AList : list) : table
	// operator ToTable(const AList : list, const AColumnName : Name) : table
	// operator ToTable(const AList : list, const AColumnName : Name, const ASequenceName : Name) : table
	public class ListToTableNode : TableNode
	{
		public const string CDefaultColumnName = "value";
		public const string CDefaultSequenceName = "sequence";
		
		public override void DetermineDataType(Plan APlan)
		{
			DetermineModifiers(APlan);
			FDataType = new Schema.TableType();
			FTableVar = new Schema.ResultTableVar(this);
			FTableVar.Owner = APlan.User;
			
			Schema.ListType LListType = (Schema.ListType)Nodes[0].DataType;
			if (LListType.ElementType is Schema.RowType)
			{
				// If given a list of rows, use the row's columns for the table
				foreach (Schema.Column LColumn in ((Schema.RowType)LListType.ElementType).Columns)
					DataType.Columns.Add(LColumn.Copy());
			}
			else
			{
				// Determine the name for the value column
				string LColumnName = CDefaultColumnName;
				if (Nodes.Count >= 2)
				{
					LColumnName = (string)APlan.EvaluateLiteralArgument(Nodes[1], "AColumnName");
					SystemNameSelectorNode.CheckValidName(LColumnName);
				}
				
				DataType.Columns.Add(new Schema.Column(LColumnName, LListType.ElementType));
			}

			// Determine the sequence column name
			string LSequenceName = CDefaultSequenceName;
			if (Nodes.Count == 3)
			{
				LSequenceName = (string)APlan.EvaluateLiteralArgument(Nodes[2], "ASequenceName");
				SystemNameSelectorNode.CheckValidName(LSequenceName);
			}
			
			// Add sequence column and make it the key
			int LSequencePrefix = 0;
			while (DataType.Columns.ContainsName(BuildName(LSequenceName, LSequencePrefix)))
				LSequencePrefix++;
			Schema.Column LSequenceColumn = new Schema.Column(BuildName(LSequenceName, LSequencePrefix), APlan.DataTypes.SystemInteger);
			DataType.Columns.Add(LSequenceColumn);
			FTableVar.EnsureTableVarColumns();
			FTableVar.Keys.Add(new Schema.Key(new Schema.TableVarColumn[] { FTableVar.Columns[LSequenceColumn.Name] }));

			TableVar.DetermineRemotable(APlan.CatalogDeviceSession);
			Order = Compiler.FindClusteringOrder(APlan, TableVar);
			
			// Ensure the order exists in the orders list
			if (!TableVar.Orders.Contains(Order))
				TableVar.Orders.Add(Order);
		}

		private static string BuildName(string ASequenceName, int ASequencePrefix)
		{
			return ASequenceName + (ASequencePrefix == 0 ? "" : ASequencePrefix.ToString());
		}
		
		public override object InternalExecute(Program AProgram)
		{
			LocalTable LResult = new LocalTable(this, AProgram);
			try
			{
				LResult.Open();
				
				using (ListValue LListValue = Nodes[0].Execute(AProgram) as ListValue)
				{
					if (LListValue.DataType.ElementType is Schema.RowType)
					{
						for (int LIndex = 0; LIndex < LListValue.Count(); LIndex++)
						{
							Row LRow = new Row(AProgram.ValueManager, DataType.RowType);
							(LListValue[LIndex] as Row).CopyTo(LRow);
							LRow[DataType.RowType.Columns.Count - 1] = LIndex;
							LResult.Insert(LRow);
						}
					}
					else
					{
						for (int LIndex = 0; LIndex < LListValue.Count(); LIndex++)
						{
							Row LRow = new Row(AProgram.ValueManager, DataType.RowType);
							LRow[0] = LListValue[LIndex];
							LRow[1] = LIndex;
							LResult.Insert(LRow);
						}
					}
				}
				
				LResult.First();
				
				return LResult;
			}
			catch
			{
				LResult.Dispose();
				throw;
			}
		}
	}
	
	// operator ToList(const ATable : cursor) : list
	public class TableToListNode : InstructionNodeBase
	{
		public override void DetermineDataType(Plan APlan)
		{
			FDataType = new Schema.ListType(((Schema.CursorType)Nodes[0].DataType).TableType.RowType);
		}
		
		protected bool CursorNext(Program AProgram, Cursor ACursor)
		{
			ACursor.SwitchContext(AProgram);
			try
			{
				return ACursor.Table.Next();
			}
			finally
			{
				ACursor.SwitchContext(AProgram);
			}
		}
		
		protected Row CursorSelect(Program AProgram, Cursor ACursor)
		{
			ACursor.SwitchContext(AProgram);
			try
			{
				return ACursor.Table.Select();
			}
			finally
			{
				ACursor.SwitchContext(AProgram);
			}
		}

		public override object InternalExecute(Program AProgram)
		{
			Cursor LCursor = AProgram.CursorManager.GetCursor(((CursorValue)Nodes[0].Execute(AProgram)).ID);
			try
			{
				ListValue LListValue = new ListValue(AProgram.ValueManager, (Schema.IListType)FDataType);
				while (CursorNext(AProgram, LCursor))
					LListValue.Add(CursorSelect(AProgram, LCursor));

				return LListValue;
			}
			finally
			{
				AProgram.CursorManager.CloseCursor(LCursor.ID);
			}
		}
	}
}
