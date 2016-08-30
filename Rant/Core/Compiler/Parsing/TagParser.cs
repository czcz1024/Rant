﻿using System;
using System.Collections.Generic;

using Rant.Core.Compiler.Syntax;
using Rant.Core.Framework;
using Rant.Core.Stringes;
using Rant.Core.Utilities;

namespace Rant.Core.Compiler.Parsing
{
	internal class TagParser : Parser
	{
		public override IEnumerator<Parser> Parse(RantCompiler compiler, CompileContext context, TokenReader reader,
			Action<RST> actionCallback)
		{
			var nextType = reader.PeekType();

			var tagStart = reader.PrevToken;

			// replacer
			switch (nextType)
			{
				case R.Regex:
				{
					var regex = reader.Read(R.Regex, "acc-replacer-regex");
					reader.Read(R.Colon);

					var arguments = new List<RST>();

					var iterator = ReadArguments(compiler, reader, arguments);
					while (iterator.MoveNext())
					{
						yield return iterator.Current;
					}

					compiler.SetNextActionCallback(actionCallback);

					if (arguments.Count != 2)
					{
						compiler.SyntaxError(Stringe.Range(tagStart, reader.PrevToken), false, "err-compiler-replacer-argcount");
						yield break;
					}

					actionCallback(new RstReplacer(regex, Util.ParseRegex(regex.Value), arguments[0], arguments[1]));
				}
					break;
				case R.Dollar:
				{
					reader.ReadToken();
					var e = ParseSubroutine(compiler, context, reader, actionCallback);
					while (e.MoveNext())
					{
						yield return e.Current;
					}
				}
					break;
				default:
				{
					var e = ParseFunction(compiler, context, reader, actionCallback);
					while (e.MoveNext())
					{
						yield return e.Current;
					}
				}
					break;
			}
		}

		private IEnumerator<Parser> ParseFunction(RantCompiler compiler, CompileContext context, TokenReader reader,
			Action<RST> actionCallback)
		{
			var functionName = reader.Read(R.Text, "acc-function-name");

			var arguments = new List<RST>();

			if (reader.PeekType() == R.Colon)
			{
				reader.ReadToken();

				var iterator = ReadArguments(compiler, reader, arguments);
				while (iterator.MoveNext())
				{
					yield return iterator.Current;
				}

				compiler.SetNextActionCallback(actionCallback);
			}
			else
			{
				reader.Read(R.RightSquare);
			}

			RantFunctionSignature sig = null;

			if (functionName != null)
			{
				if (!RantFunctionRegistry.FunctionExists(functionName.Value))
				{
					compiler.SyntaxError(functionName, false, "err-compiler-nonexistent-function", functionName.Value);
					yield break;
				}

				if ((sig = RantFunctionRegistry.GetFunction(functionName.Value, arguments.Count)) == null)
				{
					compiler.SyntaxError(functionName, false, "err-compiler-nonexistent-overload", functionName?.Value, arguments.Count);
					yield break;
				}

				actionCallback(new RstFunction(functionName, sig, arguments));
			}
		}

		private IEnumerator<Parser> ParseSubroutine(RantCompiler compiler, CompileContext context, TokenReader reader,
			Action<RST> actionCallback)
		{
			// subroutine definition
			if (reader.TakeLoose(R.LeftSquare, false))
			{
				bool inModule = false;

				if (reader.TakeLoose(R.Period))
				{
					inModule = true;
					compiler.HasModule = true;
				}
				var subroutineName = reader.ReadLoose(R.Text, "acc-subroutine-name");
				var subroutine = new RstDefineSubroutine(subroutineName)
				{
					Parameters = new Dictionary<string, SubroutineParameterType>()
				};

				if (reader.PeekLooseToken().ID == R.Colon)
				{
					reader.ReadLooseToken();

					do
					{
						var type = SubroutineParameterType.Greedy;
						if (reader.TakeLoose(R.At))
						{
							type = SubroutineParameterType.Loose;
						}

						subroutine.Parameters[reader.ReadLoose(R.Text, "argument name").Value] = type;
					} while (reader.TakeLoose(R.Semicolon, false));
				}

				reader.ReadLoose(R.RightSquare);

				var bodyStart = reader.ReadLoose(R.Colon);

				var actions = new List<RST>();
				Action<RST> bodyActionCallback = action => actions.Add(action);

				compiler.AddContext(CompileContext.SubroutineBody);
				compiler.SetNextActionCallback(bodyActionCallback);
				yield return Get<SequenceParser>();
				compiler.SetNextActionCallback(actionCallback);

				subroutine.Body = new RstSequence(actions, bodyStart);
				if (inModule)
				{
					compiler.Module.AddActionFunction(subroutineName.Value, subroutine);
				}
				actionCallback(subroutine);
			}
			else
			{
				// subroutine call
				var subroutineName = reader.Read(R.Text, "acc-subroutine-name");
				string moduleFunctionName = null;

				if (reader.TakeLoose(R.Period, false))
				{
					moduleFunctionName = reader.Read(R.Text, "module function name").Value;
				}

				var arguments = new List<RST>();

				if (reader.PeekType() == R.Colon)
				{
					reader.ReadToken();

					var iterator = ReadArguments(compiler, reader, arguments);
					while (iterator.MoveNext())
					{
						yield return iterator.Current;
					}

					compiler.SetNextActionCallback(actionCallback);
				}
				else
				{
					reader.Read(R.RightSquare);
				}

				var subroutine = new RstCallSubroutine(subroutineName, moduleFunctionName) { Arguments = arguments };

				actionCallback(subroutine);
			}
		}

		private IEnumerator<Parser> ReadArguments(RantCompiler compiler, TokenReader reader, List<RST> arguments)
		{
			var actions = new List<RST>();

			Action<RST> argActionCallback = action => actions.Add(action);
			compiler.SetNextActionCallback(argActionCallback);
			compiler.AddContext(CompileContext.FunctionEndContext);
			compiler.AddContext(CompileContext.ArgumentSequence);

			while (compiler.NextContext == CompileContext.ArgumentSequence)
			{
				var startToken = reader.PeekToken();
				yield return Get<SequenceParser>();
				arguments.Add(new RstSequence(actions, startToken));
				actions.Clear();
			}

			compiler.LeaveContext();
		}
	}
}