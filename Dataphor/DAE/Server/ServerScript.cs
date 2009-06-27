﻿/*
	Alphora Dataphor
	© Copyright 2000-2009 Alphora
	This file is licensed under a modified BSD-license which can be found here: http://dataphor.org/dataphor_license.txt
*/

using System;
using System.Text;
using System.Collections.Generic;

using Alphora.Dataphor.DAE.Language;
using Alphora.Dataphor.DAE.Runtime;

namespace Alphora.Dataphor.DAE.Server
{
	public class ServerScript : ServerChildObject, IServerScript, IRemoteServerScript
	{
		internal ServerScript(ServerProcess AProcess, string AScript) : base()
		{
			FProcess = AProcess;
			FBatches = new ServerBatches();
			FMessages = new ParserMessages();
			FScript = FProcess.ParseScript(AScript, FMessages);
			if ((FScript is Block) && (((Block)FScript).Statements.Count > 0))
			{
				Block LBlock = (Block)FScript;
						
				for (int LIndex = 0; LIndex < LBlock.Statements.Count; LIndex++)
					FBatches.Add(new ServerBatch(this, LBlock.Statements[LIndex]));
			}
			else
				FBatches.Add(new ServerBatch(this, FScript));
		}
		
		protected void UnprepareBatches()
		{
			Exception LException = null;
			while (FBatches.Count > 0)
			{
				try
				{
					FBatches.DisownAt(0).Dispose();
				}
				catch (Exception E)
				{
					LException = E;
				}			
			}
			if (LException != null)
				throw LException;
		}
		
		protected override void Dispose(bool ADisposing)
		{
			try
			{
				if (FBatches != null)
				{
					UnprepareBatches();
					FBatches.Dispose();
					FBatches = null;
				}
			}
			finally
			{
				FScript = null;
				FMessages = null;
				FProcess = null;
			
				base.Dispose(ADisposing);
			}
		}

		private Statement FScript;
		
		// Process        
		private ServerProcess FProcess;
		public ServerProcess Process { get { return FProcess; } }
		
		// Used by the ExecuteAsync node to indicate whether the process was implicitly created to run this script
		private bool FShouldCleanupProcess = false;
		public bool ShouldCleanupProcess { get { return FShouldCleanupProcess; } set { FShouldCleanupProcess = value; } }

		IServerProcess IServerScript.Process { get { return (IServerProcess)FProcess; } }
		
		IRemoteServerProcess IRemoteServerScript.Process  { get { return (IRemoteServerProcess)FProcess; } }

		// Messages
		private ParserMessages FMessages;
		ParserMessages IServerScript.Messages { get { return FMessages; } }
		
		Exception[] IRemoteServerScript.Messages
		{
			get
			{
				Exception[] LMessages = new Exception[FMessages.Count];
				FMessages.CopyTo(LMessages);
				return LMessages;
			}
		}
		
		public void CheckParsed()
		{
			if (FMessages.HasErrors())
				throw new ServerException(ServerException.Codes.UnparsedScript, FMessages.ToString());
		}
		
		// Batches
		private ServerBatches FBatches; 
		public ServerBatches Batches { get { return FBatches; } }

		IServerBatches IServerScriptBase.Batches { get { return FBatches; } }
		
		void IServerScript.Execute(DataParams AParams)
		{
			foreach (ServerBatch LBatch in FBatches)
				((IServerBatch)LBatch).Execute(AParams);
		}
		
		void IRemoteServerScript.Execute(ref RemoteParamData AParams, ProcessCallInfo ACallInfo)
		{
			FProcess.ProcessCallInfo(ACallInfo);
			foreach (ServerBatch LBatch in FBatches)
				((IRemoteServerBatch)LBatch).Execute(ref AParams, FProcess.EmptyCallInfo());
		}
	}
	
	// ServerScripts
	public class ServerScripts : ServerChildObjects
	{		
		protected override void Validate(ServerChildObject AObject)
		{
			if (!(AObject is ServerScript))
				throw new ServerException(ServerException.Codes.ServerScriptContainer);
		}
		
		public new ServerScript this[int AIndex]
		{
			get { return (ServerScript)base[AIndex]; } 
			set { base[AIndex] = value; }
		}
	}
}
