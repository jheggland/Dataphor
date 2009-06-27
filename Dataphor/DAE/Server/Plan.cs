﻿/*
	Alphora Dataphor
	© Copyright 2000-2009 Alphora
	This file is licensed under a modified BSD-license which can be found here: http://dataphor.org/dataphor_license.txt
*/

using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

using Alphora.Dataphor.DAE.Language;
using Alphora.Dataphor.DAE.Language.D4;
using Alphora.Dataphor.DAE.Streams;
using Alphora.Dataphor.DAE.Runtime;
using Alphora.Dataphor.DAE.Runtime.Data;
using Alphora.Dataphor.DAE.Runtime.Instructions;

namespace Alphora.Dataphor.DAE.Server
{
	// Plan provides compile time state	for the compiler.
	public class Plan : Disposable
	{
		public Plan(ServerProcess AServerProcess) : base()
		{
			FServerProcess = AServerProcess;
			FCatalogLocks = new ArrayList();
			FSymbols = new Context(FServerProcess.Context.MaxStackDepth, FServerProcess.Context.MaxCallDepth);
			PushSecurityContext(new SecurityContext(FServerProcess.ServerSession.User));
			PushStatementContext(new StatementContext(StatementType.Select));
		}
		
		protected override void Dispose(bool ADisposing)
		{
			try
			{
				try
				{
					try
					{
						try
						{
							try
							{
								if (FStatementContexts.Count > 0)
									PopStatementContext();
									
							}
							finally
							{
								if (FSecurityContexts.Count > 0)
									PopSecurityContext();
							}
						}
						finally
						{
							if (FCatalogLocks != null)
							{
								ReleaseCatalogLocks();
								FCatalogLocks = null;
							}
						}
					}
					finally
					{
						if (FSymbols != null)
						{
							FSymbols.Dispose();
							FSymbols = null;
						}
					}
				}
				finally
				{
					if (FDevicePlans != null)
					{
						Schema.DevicePlan LDevicePlan;
						foreach (object LObject in FDevicePlans.Keys)
						{
							LDevicePlan = (Schema.DevicePlan)FDevicePlans[LObject];
							LDevicePlan.Device.Unprepare(LDevicePlan);
						}
						FDevicePlans = null;
					}
				}
			}
			finally
			{
				FServerProcess = null;

				base.Dispose(ADisposing);
			}
		}

		// Process        
		protected ServerProcess FServerProcess;
		public ServerProcess ServerProcess { get { return FServerProcess; } }
		
		public void BindToProcess(ServerProcess AProcess)
		{
			PopSecurityContext();
			PushSecurityContext(new SecurityContext(AProcess.ServerSession.User));
			FServerProcess = AProcess;
			
			// Reset execution statistics
			FStatistics.ExecuteTime = TimeSpan.Zero;
			FStatistics.DeviceExecuteTime = TimeSpan.Zero;
		}
		
		public void CheckCompiled()
		{
			if (FMessages.HasErrors)
				if (FMessages.Count == 1)
					throw FMessages[0];
				else
					throw new ServerException(ServerException.Codes.UncompiledPlan, FMessages.ToString(CompilerErrorLevel.NonFatal));
		}
		
		// Statistics
		private PlanStatistics FStatistics = new PlanStatistics();
		public PlanStatistics Statistics { get { return FStatistics; } }
		
		// DevicePlans
		private Hashtable FDevicePlans = new Hashtable();

		// GetDevicePlan
		public Schema.DevicePlan GetDevicePlan(PlanNode APlanNode)
		{
			object LDevicePlan = FDevicePlans[APlanNode];
			if (LDevicePlan == null)
			{
				FServerProcess.EnsureDeviceStarted(APlanNode.Device);
				Schema.DevicePlan LNewDevicePlan = APlanNode.Device.Prepare(this, APlanNode);
				AddDevicePlan(LNewDevicePlan);
				return LNewDevicePlan;
			}
			return (Schema.DevicePlan)LDevicePlan;
		}
		
		// AddDevicePlan
		public void AddDevicePlan(Schema.DevicePlan ADevicePlan)
		{
			if (!FDevicePlans.Contains(ADevicePlan.Node))
				FDevicePlans.Add(ADevicePlan.Node, ADevicePlan);
		}
		
		// CatalogLocks
		protected ArrayList FCatalogLocks; // cannot be a hash table because it must be able to contain multiple entries for the same LockID
		public void AcquireCatalogLock(Schema.Object AObject, LockMode AMode)
		{
			#if USECATALOGLOCKS
			LockID LLockID = new LockID(Server.CCatalogManagerID, AObject.Name);
			if (FServerProcess.ServerSession.Server.LockManager.LockImmediate(FServerProcess.ProcessID, LLockID, AMode))
				FCatalogLocks.Add(LLockID);
			else
				throw new RuntimeException(RuntimeException.Codes.UnableToLockObject, AObject);
			#endif
		}
		
		public void ReleaseCatalogLock(Schema.Object AObject)
		{
			#if USECATALOGLOCKS
			LockID LLockID = new LockID(Server.CCatalogManagerID, AObject.Name);
			if (FServerProcess.ServerSession.Server.LockManager.IsLocked(LLockID))
				FServerProcess.ServerSession.Server.LockManager.Unlock(FServerProcess.ProcessID, LLockID);
			#endif
		}
        
		protected void ReleaseCatalogLocks()
		{
			#if USECATALOGLOCKS
			for (int LIndex = FCatalogLocks.Count - 1; LIndex >= 0; LIndex--)
			{
				if (FServerProcess.ServerSession.Server.LockManager.IsLocked((LockID)FCatalogLocks[LIndex]))
					FServerProcess.ServerSession.Server.LockManager.Unlock(FServerProcess.ProcessID, (LockID)FCatalogLocks[LIndex]);
				FCatalogLocks.RemoveAt(LIndex);
			}
			#endif
		}

		// Symbols        
		protected Context FSymbols;
		public Context Symbols { get { return FSymbols; } }
        
		// LoopContext
		protected int FLoopCount;
		public bool InLoop { get { return FLoopCount > 0; } }
        
		public void EnterLoop() 
		{ 
			FLoopCount++; 
		}
        
		public void ExitLoop() 
		{ 
			FLoopCount--; 
		}
		
		// RowContext
		public bool InRowContext { get { return FSymbols.InRowContext; } }
		
		public void EnterRowContext()
		{
			FSymbols.PushFrame(true);
		}
		
		public void ExitRowContext()
		{
			FSymbols.PopFrame();
		}
		
		// ATCreationContext
		private int FATCreationContext;
		/// <summary>Indicates whether the current plan is executing a statement to create an A/T translated object.</summary>
		/// <remarks>
		/// This context is needed because it is not always the case that A/T objects will be being created (or recreated 
		/// such as when view references are reinferred) inside of an A/T. By checking for this context, we are ensured
		/// that things that should not be checked for A/T objects (such as errors about derived references not existing in
		/// adorn expressions, etc.,.) will not occur.
		/// </remarks>
		public bool InATCreationContext { get { return FATCreationContext > 0; } }
		
		public void PushATCreationContext()
		{
			FATCreationContext++;
		}
		
		public void PopATCreationContext()
		{
			FATCreationContext--;
		}
		
		/// <summary>Indicates whether time stamps should be affected by alter and drop table variable and operator statements.</summary>
		public bool ShouldAffectTimeStamp { get { return ServerProcess.ShouldAffectTimeStamp; } }
		
		public void EnterTimeStampSafeContext()
		{
			ServerProcess.EnterTimeStampSafeContext();
		}
		
		public void ExitTimeStampSafeContext()
		{
			ServerProcess.ExitTimeStampSafeContext();
		}
		
		protected int FTypeOfCount;
		public bool InTypeOfContext { get { return FTypeOfCount > 0; } }
		
		public void PushTypeOfContext()
		{
			FTypeOfCount++;
		}
		
		public void PopTypeOfContext()
		{
			FTypeOfCount--;
		}
		
		// TypeContext
		protected ArrayList FTypeStack = new ArrayList();
		
		public void PushTypeContext(Schema.IDataType ADataType)
		{
			FTypeStack.Add(ADataType);
		}
		
		public void PopTypeContext(Schema.IDataType ADataType)
		{
			Error.AssertFail(FTypeStack.Count > 0, "Type stack underflow");
			FTypeStack.RemoveAt(FTypeStack.Count - 1);
		}
		
		public bool InScalarTypeContext()
		{
			return (FTypeStack.Count > 0) && (FTypeStack[FTypeStack.Count - 1] is Schema.IScalarType);
		}
		
		public bool InRowTypeContext()
		{
			return (FTypeStack.Count > 0) && (FTypeStack[FTypeStack.Count - 1] is Schema.IRowType);
		}
		
		public bool InTableTypeContext()
		{
			return (FTypeStack.Count > 0) && (FTypeStack[FTypeStack.Count - 1] is Schema.ITableType);
		}

		public bool InListTypeContext()
		{
			return (FTypeStack.Count > 0) && (FTypeStack[FTypeStack.Count - 1] is Schema.IListType);
		}
		
		// CurrentStatement
		protected ArrayList FStatementStack = new ArrayList();
		
		public void PushStatement(Statement AStatement)
		{
			FStatementStack.Add(AStatement);
		}
		
		public void PopStatement()
		{
			Error.AssertFail(FStatementStack.Count > 0, "Statement stack underflow");
			FStatementStack.RemoveAt(FStatementStack.Count - 1);
		}
		
		/// <remarks>Returns the current statement in the abstract syntax tree being compiled.  Will return null if no statement is on the statement stack.</remarks>
		public Statement CurrentStatement()
		{
			if (FStatementStack.Count > 0)
				return (Statement)FStatementStack[FStatementStack.Count - 1];
			return null;
		}

		// CursorContext
		protected CursorContexts FCursorContexts = new CursorContexts();
		public void PushCursorContext(CursorContext AContext)
		{
			FCursorContexts.Add(AContext);
		}
        
		public void PopCursorContext()
		{
			FCursorContexts.RemoveAt(FCursorContexts.Count - 1);
		}
        
		public CursorContext GetDefaultCursorContext()
		{
			return new CursorContext(CursorType.Dynamic, CursorCapability.Navigable, CursorIsolation.None);
		}
        
		public CursorContext CursorContext
		{
			get
			{
				if (FCursorContexts.Count > 0)
					return FCursorContexts[FCursorContexts.Count - 1];
				else
					return GetDefaultCursorContext();
			}
		}
		
		// StatementContext
		protected StatementContexts FStatementContexts = new StatementContexts();
		public void PushStatementContext(StatementContext AContext)
		{
			FStatementContexts.Add(AContext);
		}
		
		public void PopStatementContext()
		{
			FStatementContexts.RemoveAt(FStatementContexts.Count - 1);
		}
		
		public StatementContext GetDefaultStatementContext()
		{
			return new StatementContext(StatementType.Select);
		}
		
		public bool HasStatementContext { get { return FStatementContexts.Count > 0; } }

		public StatementContext StatementContext { get { return FStatementContexts[FStatementContexts.Count - 1]; } }
		
		// SecurityContext
		protected SecurityContexts FSecurityContexts = new SecurityContexts();
		public void PushSecurityContext(SecurityContext AContext)
		{
			FSecurityContexts.Add(AContext);
		}
		
		public void PopSecurityContext()
		{
			FSecurityContexts.RemoveAt(FSecurityContexts.Count - 1);
		}
		
		public bool HasSecurityContext { get { return FSecurityContexts.Count > 0; } }
		
		public SecurityContext SecurityContext { get { return FSecurityContexts[FSecurityContexts.Count - 1]; } }
		
		public void UpdateSecurityContexts(Schema.User AUser)
		{
			for (int LIndex = 0; LIndex < FSecurityContexts.Count; LIndex++)
				if (FSecurityContexts[LIndex].User.ID == AUser.ID)
					FSecurityContexts[LIndex].SetUser(AUser);
		}
		
		// A Temporary catalog where catalog objects are registered during compilation
		protected Schema.Catalog FPlanCatalog;
		public Schema.Catalog PlanCatalog
		{
			get
			{
				if (FPlanCatalog == null)
					FPlanCatalog = new Schema.Catalog();
				return FPlanCatalog;
			}
		}
		
		// A temporary list of session table variables created during compilation
		protected Schema.Objects FPlanSessionObjects;
		public Schema.Objects PlanSessionObjects 
		{
			get
			{
				if (FPlanSessionObjects  == null)
					FPlanSessionObjects = new Schema.Objects();
				return FPlanSessionObjects;
			}
		}
		
		protected Schema.Objects FPlanSessionOperators;
		public Schema.Objects PlanSessionOperators
		{
			get
			{
				if (FPlanSessionOperators == null)
					FPlanSessionOperators = new Schema.Objects();
				return FPlanSessionOperators;
			}
		}

		// Catalog		
		public Schema.Catalog Catalog { get { return ServerProcess.ServerSession.Server.Catalog; } }
		
		public Schema.User User { get { return SecurityContext.User; } }
		
		public Schema.LoadedLibrary CurrentLibrary { get { return ServerProcess.ServerSession.CurrentLibrary; } }
		
		public Schema.NameResolutionPath NameResolutionPath { get { return ServerProcess.ServerSession.CurrentLibrary.GetNameResolutionPath(ServerProcess.ServerSession.Server.SystemLibrary); } }
		
		public IStreamManager StreamManager { get { return (IStreamManager)ServerProcess; } }

		public Schema.Device TempDevice { get { return ServerProcess.ServerSession.Server.TempDevice; } }
		
		#if USEHEAPDEVICE
		public Schema.Device HeapDevice { get { return ServerProcess.ServerSession.Server.HeapDevice; } }
		#endif
		
		public CursorManager CursorManager { get { return ServerProcess.ServerSession.CursorManager; } }

		public string DefaultTagNameSpace { get { return String.Empty; } }
		
		public string DefaultDeviceName { get { return GetDefaultDeviceName(ServerProcess.ServerSession.CurrentLibrary.Name, false); } }
		
		public string GetDefaultDeviceName(string ALibraryName, bool AShouldThrow)
		{
			return GetDefaultDeviceName(Catalog.Libraries[ALibraryName], AShouldThrow);
		}
		
		protected string GetDefaultDeviceName(Schema.Library ALibrary, bool AShouldThrow)
		{
			if (ALibrary.DefaultDeviceName != String.Empty)
				return ALibrary.DefaultDeviceName;
			
			Schema.Libraries LLibraries = new Schema.Libraries();
			LLibraries.Add(ALibrary);
			return GetDefaultDeviceName(LLibraries, AShouldThrow);
		}
		
		protected string GetDefaultDeviceName(Schema.Libraries ALibraries, bool AShouldThrow)
		{
			while (ALibraries.Count > 0)
			{
				Schema.Library LLibrary = ALibraries[0];
				ALibraries.RemoveAt(0);
				
				string LDefaultDeviceName = String.Empty;
				Schema.Library LRequiredLibrary;
				foreach (Schema.LibraryReference LLibraryReference in LLibrary.Libraries)
				{
					LRequiredLibrary = Catalog.Libraries[LLibraryReference.Name];
					if (LRequiredLibrary.DefaultDeviceName != String.Empty)
					{
						if (LDefaultDeviceName != String.Empty)
							if (AShouldThrow)
								throw new Schema.SchemaException(Schema.SchemaException.Codes.AmbiguousDefaultDeviceName, LLibrary.Name);
							else
								return String.Empty;
						LDefaultDeviceName = LRequiredLibrary.DefaultDeviceName;
					}
					else
						if (!ALibraries.Contains(LRequiredLibrary))
							ALibraries.Add(LRequiredLibrary);
				}
					
				if (LDefaultDeviceName != String.Empty)
					return LDefaultDeviceName;
			}
			
			return String.Empty;
		}

		public void CheckRight(string ARight)
		{
			if (!InATCreationContext)
				ServerProcess.CatalogDeviceSession.CheckUserHasRight(SecurityContext.User.ID, ARight);
		}
		
		public bool HasRight(string ARight)
		{
			return InATCreationContext || ServerProcess.CatalogDeviceSession.UserHasRight(SecurityContext.User.ID, ARight);
		}

		/// <summary>Raises an error if the current user is not authorized to administer the given user.</summary>		
		public void CheckAuthorized(string AUserID)
		{
			if (!User.IsAdminUser() || !User.IsSystemUser() || (String.Compare(AUserID, User.ID, true) != 0))
				CheckRight(Schema.RightNames.AlterUser);
		}
		
		// During compilation, the creation object stack maintains a context for the
		// object currently being created.  References into the catalog register dependencies
		// against this object as they are encountered by the compiler.
		protected ArrayList FCreationObjects;
		public void PushCreationObject(Schema.Object AObject)
		{
			if (FCreationObjects == null)
				FCreationObjects = new ArrayList();
			FCreationObjects.Add(AObject);
		}
		
		public void PopCreationObject()
		{
			FCreationObjects.RemoveAt(FCreationObjects.Count - 1);
		}
		
		public Schema.Object CurrentCreationObject()
		{
			if ((FCreationObjects == null) || (FCreationObjects.Count == 0))
				return null;
			return (Schema.Object)FCreationObjects[FCreationObjects.Count - 1];
		}
		
		public bool IsOperatorCreationContext
		{
			get
			{
				return 
					(FCreationObjects != null) && 
					(FCreationObjects.Count > 0) && 
					(FCreationObjects[FCreationObjects.Count - 1] is Schema.Operator);
			}
		}
		
		public void CheckClassDependency(ClassDefinition AClassDefinition)
		{
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Object LCreationObject = (Schema.Object)FCreationObjects[FCreationObjects.Count - 1];
				if 
				(
					(LCreationObject is Schema.CatalogObject) && 
					(((Schema.CatalogObject)LCreationObject).Library != null) &&
					(((Schema.CatalogObject)LCreationObject).SessionObjectName == null) && 
					(
						!(LCreationObject is Schema.TableVar) || 
						(((Schema.TableVar)LCreationObject).SourceTableName == null)
					)
				)
				{
					CheckClassDependency(((Schema.CatalogObject)LCreationObject).Library, AClassDefinition);
				}
			}
		}
		
		public void CheckClassDependency(Schema.LoadedLibrary ALibrary, ClassDefinition AClassDefinition)
		{
			Schema.RegisteredClass LClass = Catalog.ClassLoader.GetClass(AClassDefinition);
			if (!ALibrary.IsRequiredLibrary(LClass.Library))
				throw new Schema.SchemaException(Schema.SchemaException.Codes.NonRequiredClassDependency, LClass.Name, ALibrary.Name, LClass.Library.Name); // TODO: Better exception
		}
		
		public void AttachDependencies(Schema.ObjectList ADependencies)
		{
			for (int LIndex = 0; LIndex < ADependencies.Count; LIndex++)
				AttachDependency(ADependencies.ResolveObject(ServerProcess, LIndex));
		}
		
		public void AttachDependency(Schema.Object AObject)
		{
			if (FCreationObjects == null)
				FCreationObjects = new ArrayList();
			if (FCreationObjects.Count > 0)
			{
				// If this is a generated object, attach the dependency to the generator, rather than the object directly.
				// Unless it is a device object, in which case the dependency should be attached to the generated object.
				if (AObject.IsGenerated && !(AObject is Schema.DeviceObject) && (AObject.GeneratorID >= 0))
					AObject = AObject.ResolveGenerator(ServerProcess);
					
				if (AObject is Schema.Property)
					AObject = ((Schema.Property)AObject).Representation;
					
				Schema.Object LObject = (Schema.Object)FCreationObjects[FCreationObjects.Count - 1];
				if ((LObject != AObject) && (!(AObject is Schema.Reference) || (LObject is Schema.TableVar)) && (!LObject.HasDependencies() || !LObject.Dependencies.Contains(AObject.ID)))
				{
					if (!ServerProcess.ServerSession.Server.IsRepository)
					{
						if 
						(
							(LObject.Library != null) &&
							!LObject.IsSessionObject &&
							!LObject.IsATObject
						)
						{
							Schema.LoadedLibrary LLibrary = LObject.Library;
							Schema.LoadedLibrary LDependentLibrary = AObject.Library;
							if (LDependentLibrary == null)
								throw new Schema.SchemaException(Schema.SchemaException.Codes.NonLibraryDependency, LObject.Name, LLibrary.Name, AObject.Name);
							if (!LLibrary.IsRequiredLibrary(LDependentLibrary) && (String.Compare(LDependentLibrary.Name, Server.CSystemLibraryName, false) != 0)) // Ignore dependencies to the system library, these are implicitly available
								throw new Schema.SchemaException(Schema.SchemaException.Codes.NonRequiredLibraryDependency, LObject.Name, LLibrary.Name, AObject.Name, LDependentLibrary.Name);
						}
						
						// if LObject is not a session object or an AT object, AObject cannot be a session object
						if ((LObject is Schema.CatalogObject) && !LObject.IsSessionObject && !LObject.IsATObject && AObject.IsSessionObject)
							throw new Schema.SchemaException(Schema.SchemaException.Codes.SessionObjectDependency, LObject.Name, ((Schema.CatalogObject)AObject).SessionObjectName);
								
						// if LObject is not a generated or an AT object or a plan object (Name == SessionObjectName), AObject cannot be an AT object
						if (((LObject is Schema.CatalogObject) && (((Schema.CatalogObject)LObject).SessionObjectName != LObject.Name)) && !LObject.IsGenerated && !LObject.IsATObject && AObject.IsATObject)
							if (AObject is Schema.TableVar)
								throw new Schema.SchemaException(Schema.SchemaException.Codes.ApplicationTransactionObjectDependency, LObject.Name, ((Schema.TableVar)AObject).SourceTableName);
							else
								throw new Schema.SchemaException(Schema.SchemaException.Codes.ApplicationTransactionObjectDependency, LObject.Name, ((Schema.Operator)AObject).SourceOperatorName);
					}
							
					Error.AssertFail(AObject.ID > 0, "Object {0} does not have an object id and cannot be tracked as a dependency.", AObject.Name);
					LObject.AddDependency(AObject);
				}
			}
		}
		
		public void SetIsLiteral(bool AIsLiteral)
		{		
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Operator LOperator = FCreationObjects[FCreationObjects.Count - 1] as Schema.Operator;
				if (LOperator != null)
					LOperator.IsLiteral = LOperator.IsLiteral && AIsLiteral;
			}
		}
		
		public void SetIsFunctional(bool AIsFunctional)
		{
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Operator LOperator = FCreationObjects[FCreationObjects.Count - 1] as Schema.Operator;
				if (LOperator != null)
					LOperator.IsFunctional = LOperator.IsFunctional && AIsFunctional;
			}
		}
		
		public void SetIsDeterministic(bool AIsDeterministic)
		{
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Operator LOperator = FCreationObjects[FCreationObjects.Count - 1] as Schema.Operator;
				if (LOperator != null)
					LOperator.IsDeterministic = LOperator.IsDeterministic && AIsDeterministic;
			}
		}
		
		public void SetIsRepeatable(bool AIsRepeatable)
		{
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Operator LOperator = FCreationObjects[FCreationObjects.Count - 1] as Schema.Operator;
				if (LOperator != null)
					LOperator.IsRepeatable = LOperator.IsRepeatable && AIsRepeatable;
			}
		}
		
		public void SetIsNilable(bool AIsNilable)
		{
			if ((FCreationObjects != null) && (FCreationObjects.Count > 0))
			{
				Schema.Operator LOperator = FCreationObjects[FCreationObjects.Count - 1] as Schema.Operator;
				if (LOperator != null)
					LOperator.IsNilable = LOperator.IsNilable || AIsNilable;
			}
		}
		
		// True if the current expression is being evaluated as the target of an assignment		
		public bool IsAssignmentTarget
		{
			get 
			{ 
				return (StatementContext.StatementType == StatementType.Assignment);
			}
		}
		
		// Messages
		private CompilerMessages FMessages = new CompilerMessages();
		public CompilerMessages Messages { get { return FMessages; } }
		
		#if ACCUMULATOR
		// Performance Tracking
		public long Accumulator = 0;
		#endif
	}
}
