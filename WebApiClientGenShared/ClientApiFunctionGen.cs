﻿using System;
using System.CodeDom;
using System.Linq;
using System.Diagnostics;
using Fonlow.Reflection;
using Fonlow.Web.Meta;

namespace Fonlow.CodeDom.Web.Cs
{
	/// <summary>
	/// Generate a client function upon ApiDescription for C#
	/// </summary>
	internal class ClientApiFunctionGen
	{
		SharedContext sharedContext;
		WebApiDescription description;
		string methodName;
		protected Type returnType;
		CodeMemberMethod method;
		readonly Fonlow.Poco2Client.IPoco2Client poco2CsGen;

		bool forAsync;
		bool stringAsString;

		public ClientApiFunctionGen(SharedContext sharedContext, WebApiDescription description, Fonlow.Poco2Client.IPoco2Client poco2CsGen, bool stringAsString, bool forAsync = false)
		{
			this.description = description;
			this.sharedContext = sharedContext;
			this.poco2CsGen = poco2CsGen;
			this.forAsync = forAsync;
			this.stringAsString = stringAsString;

			methodName = description.ActionDescriptor.ActionName;
			if (methodName.EndsWith("Async"))
				methodName = methodName.Substring(0, methodName.Length - 5);

			returnType = description.ResponseDescription?.ResponseType ?? description.ActionDescriptor.ReturnType;

		}

		const string typeOfIHttpActionResult = "System.Web.Http.IHttpActionResult";
		const string typeOfIActionResult = "Microsoft.AspNetCore.Mvc.IActionResult"; //for .net core 2.1. I did not need this for .net core 2.0
		const string typeOfActionResult = "Microsoft.AspNetCore.Mvc.ActionResult"; //for .net core 2.1. I did not need this for .net core 2.0

		static readonly Type typeOfChar = typeof(char);

		public static CodeMemberMethod Create(SharedContext sharedContext, WebApiDescription description, Fonlow.Poco2Client.IPoco2Client poco2CsGen, bool stringAsString, bool forAsync)
		{
			var gen = new ClientApiFunctionGen(sharedContext, description, poco2CsGen, stringAsString, forAsync);
			return gen.CreateApiFunction();
		}

		public CodeMemberMethod CreateApiFunction()
		{
			//create method
			method = forAsync ? CreateMethodBasicForAsync() : CreateMethodBasic();

			CreateDocComments();

			switch (description.HttpMethod)
			{
				case "GET":
					if (forAsync)
					{
						RenderGetOrDeleteImplementation(
							new CodeMethodInvokeExpression(new CodeSnippetExpression("await " + sharedContext.clientReference.FieldName), "GetAsync", new CodeSnippetExpression("requestUri")));
					}
					else
					{
						RenderGetOrDeleteImplementation(
							new CodePropertyReferenceExpression(
							new CodeMethodInvokeExpression(sharedContext.clientReference, "GetAsync", new CodeSnippetExpression("requestUri")), "Result"));
					}
					break;
				case "DELETE":
					if (forAsync)
					{
						RenderGetOrDeleteImplementation(
							new CodeMethodInvokeExpression(new CodeSnippetExpression("await " + sharedContext.clientReference.FieldName), "DeleteAsync", new CodeSnippetExpression("requestUri")));
					}
					else
					{
						RenderGetOrDeleteImplementation(
							new CodePropertyReferenceExpression(
							new CodeMethodInvokeExpression(sharedContext.clientReference, "DeleteAsync", new CodeSnippetExpression("requestUri"))
							, "Result"));
					}
					break;
				case "POST":
					RenderPostOrPutImplementation(true);
					break;
				case "PUT":
					RenderPostOrPutImplementation(false);
					break;

				default:
					Trace.TraceWarning("This HTTP method {0} is not yet supported", description.HttpMethod);
					break;
			}

			return method;
		}

		CodeMemberMethod CreateMethodBasic()
		{
			return new CodeMemberMethod()
			{
				Attributes = MemberAttributes.Public | MemberAttributes.Final,
				Name = methodName,
				ReturnType = poco2CsGen.TranslateToClientTypeReference(returnType),
			};
		}

		CodeMemberMethod CreateMethodBasicForAsync()
		{
			return new CodeMemberMethod()
			{
				Attributes = MemberAttributes.Public | MemberAttributes.Final,
				Name = methodName + "Async",
				ReturnType = returnType == null ? new CodeTypeReference("async Task")
				: new CodeTypeReference("async Task", poco2CsGen.TranslateToClientTypeReference(returnType)),
			};
		}

		void CreateDocComments()
		{
			Action<string, string> CreateDocComment = (elementName, doc) =>
			{
				if (string.IsNullOrWhiteSpace(doc))
					return;

				method.Comments.Add(new CodeCommentStatement("<" + elementName + ">" + doc + "</" + elementName + ">", true));
			};

			Action<string, string> CreateParamDocComment = (paramName, doc) =>
			{
				if (String.IsNullOrWhiteSpace(doc))
					return;

				method.Comments.Add(new CodeCommentStatement("<param name=\"" + paramName + "\">" + doc + "</param>", true));
			};

			method.Comments.Add(new CodeCommentStatement("<summary>", true));
			var noIndent = Fonlow.DocComment.StringFunctions.TrimIndentedMultiLineTextToArray(description.Documentation);
			if (noIndent != null)
			{
				foreach (var item in noIndent)
				{
					method.Comments.Add(new CodeCommentStatement(item, true));
				}
			}

			method.Comments.Add(new CodeCommentStatement(description.HttpMethod + " " + description.RelativePath, true));
			method.Comments.Add(new CodeCommentStatement("</summary>", true));
			foreach (var item in description.ParameterDescriptions)
			{
				CreateParamDocComment(item.Name, item.Documentation);
			}
			CreateDocComment("returns", description.ResponseDescription.Documentation);
		}

		void RenderGetOrDeleteImplementation(CodeExpression httpMethodInvokeExpression)
		{
			//Create function parameters
			var parameters = description.ParameterDescriptions.Select(d => new CodeParameterDeclarationExpression()
			{
				Name = d.Name,
				Type = poco2CsGen.TranslateToClientTypeReference(d.ParameterDescriptor.ParameterType),

			}).ToArray();

			method.Parameters.AddRange(parameters);

			var jsUriQuery = UriQueryHelper.CreateUriQuery(description.RelativePath, description.ParameterDescriptions);
			var uriText = jsUriQuery == null ? $"new Uri(this.baseUri, \"{description.RelativePath}\")" :
				RemoveTrialEmptyString($"new Uri(this.baseUri, \"{jsUriQuery}\")");

			method.Statements.Add(new CodeVariableDeclarationStatement(
				new CodeTypeReference("var"), "requestUri",
				new CodeSnippetExpression(uriText)));

			//Statement: var result = this.client.GetAsync(requestUri.ToString()).Result;
			method.Statements.Add(new CodeVariableDeclarationStatement(
				new CodeTypeReference("var"), "responseMessage", httpMethodInvokeExpression));

			////Statement: var result = task.Result;
			var resultReference = new CodeVariableReferenceExpression("responseMessage");

			//Statement: result.EnsureSuccessStatusCode();
			method.Statements.Add(new CodeMethodInvokeExpression(resultReference, "EnsureSuccessStatusCode"));

			//Statement: return something;
			if (returnType != null)
			{
				AddReturnStatement();
			}

		}

		const string typeNameOfHttpResponseMessage = "System.Net.Http.HttpResponseMessage";

		void AddReturnStatement()
		{
			if ((returnType.FullName == typeNameOfHttpResponseMessage) || (returnType.FullName == typeOfIHttpActionResult) || (returnType.FullName == typeOfIActionResult) || (returnType.FullName == typeOfActionResult))
			{
				method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression("responseMessage")));
				return;
			}
			else if (returnType.IsGenericType)
			{
				Type genericTypeDefinition = returnType.GetGenericTypeDefinition();
				if (genericTypeDefinition == typeof(System.Threading.Tasks.Task<>))
				{
					method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression("responseMessage")));
					return;
				}
			}

			method.Statements.Add(new CodeSnippetStatement(forAsync ?
				"\t\t\tvar stream = await responseMessage.Content.ReadAsStreamAsync();"
				: "\t\t\tvar stream = responseMessage.Content.ReadAsStreamAsync().Result;"));
			//  method.Statements.Add(new CodeSnippetStatement("            using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))"));

			if (returnType != null && TypeHelper.IsStringType(returnType))
			{
				if (this.stringAsString)
				{
					method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (System.IO.StreamReader streamReader = new System.IO.StreamReader(stream))"));
					method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
					method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression("streamReader.ReadToEnd();")));
				}
				else
				{
					method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (JsonReader jsonReader = new JsonTextReader(new System.IO.StreamReader(stream)))"));
					method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
					method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression("jsonReader.ReadAsString()")));
				}
			}
			else if (returnType == typeOfChar)
			{
				method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (JsonReader jsonReader = new JsonTextReader(new System.IO.StreamReader(stream)))"));
				method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "serializer", new CodeSnippetExpression("new JsonSerializer()")));
				method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression("serializer.Deserialize<char>(jsonReader)")));
			}
			else if (returnType.IsPrimitive)
			{
				method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (JsonReader jsonReader = new JsonTextReader(new System.IO.StreamReader(stream)))"));
				method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
				method.Statements.Add(new CodeMethodReturnStatement(new CodeSnippetExpression(String.Format("{0}.Parse(jsonReader.ReadAsString())", returnType.FullName))));
			}
			else if (returnType.IsGenericType)
			{
				method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (JsonReader jsonReader = new JsonTextReader(new System.IO.StreamReader(stream)))"));
				method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "serializer", new CodeSnippetExpression("new JsonSerializer()")));
				method.Statements.Add(new CodeMethodReturnStatement(new CodeMethodInvokeExpression(
					new CodeMethodReferenceExpression(new CodeVariableReferenceExpression("serializer"), "Deserialize", poco2CsGen.TranslateToClientTypeReference(returnType)),
						new CodeSnippetExpression("jsonReader"))));
			}
			else if (TypeHelper.IsComplexType(returnType))
			{
				method.Statements.Add(new CodeSnippetStatement("\t\t\tusing (JsonReader jsonReader = new JsonTextReader(new System.IO.StreamReader(stream)))"));
				method.Statements.Add(new CodeSnippetStatement("\t\t\t{"));
				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "serializer", new CodeSnippetExpression("new JsonSerializer()")));
				method.Statements.Add(new CodeMethodReturnStatement(new CodeMethodInvokeExpression(
					new CodeMethodReferenceExpression(new CodeVariableReferenceExpression("serializer"), "Deserialize", poco2CsGen.TranslateToClientTypeReference(returnType)),
						new CodeSnippetExpression("jsonReader"))));
			}
			else
			{
				Trace.TraceWarning("This type is not yet supported: {0}", returnType.FullName);
			}

			method.Statements.Add(new CodeSnippetStatement("\t\t\t}"));


		}

		void RenderPostOrPutImplementation(bool isPost)
		{
			//Create function parameters in prototype
			var parameters = description.ParameterDescriptions.Select(d => new CodeParameterDeclarationExpression()
			{
				Name = d.Name,
				Type = poco2CsGen.TranslateToClientTypeReference(d.ParameterDescriptor.ParameterType),

			}).ToArray();
			method.Parameters.AddRange(parameters);

			var uriQueryParameters = description.ParameterDescriptions.Where(d =>
				(d.ParameterDescriptor.ParameterBinder != ParameterBinder.FromBody && d.ParameterDescriptor.ParameterBinder != ParameterBinder.FromForm && TypeHelper.IsSimpleType(d.ParameterDescriptor.ParameterType))
				|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
				|| (d.ParameterDescriptor.ParameterType.IsValueType && d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
				).Select(d => new CodeParameterDeclarationExpression()
				{
					Name = d.Name,
					Type = poco2CsGen.TranslateToClientTypeReference(d.ParameterDescriptor.ParameterType),
				}).ToArray();

			var fromBodyParameterDescriptions = description.ParameterDescriptions.Where(d => d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromBody
				|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && (!(d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri) || (d.ParameterDescriptor.ParameterBinder == ParameterBinder.None)))).ToArray();
			if (fromBodyParameterDescriptions.Length > 1)
			{
				throw new CodeGenException("Bad Api Definition")
				{
					Description = String.Format("This API function {0} has more than 1 FromBody bindings in parameters", description.ActionDescriptor.ActionName)
				};
			}

			var singleFromBodyParameterDescription = fromBodyParameterDescriptions.FirstOrDefault();

			Action AddRequestUriWithQueryAssignmentStatement = () =>
			{

				var jsUriQuery = UriQueryHelper.CreateUriQuery(description.RelativePath, description.ParameterDescriptions);
				var uriText = jsUriQuery == null ? $"new Uri(this.baseUri, \"{description.RelativePath}\")" :
				RemoveTrialEmptyString($"new Uri(this.baseUri, \"{jsUriQuery}\")");

				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "requestUri",
					new CodeSnippetExpression(uriText)));
			};

			Action AddRequestUriAssignmentStatement = () =>
			{
				var jsUriQuery = UriQueryHelper.CreateUriQuery(description.RelativePath, description.ParameterDescriptions);
				var uriText = jsUriQuery == null ? $"new Uri(this.baseUri, \"{description.RelativePath}\")" :
				RemoveTrialEmptyString($"new Uri(this.baseUri, \"{jsUriQuery}\")");

				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "requestUri",
					new CodeSnippetExpression(uriText)));

			};

			Action<CodeExpression> AddPostStatement = (httpMethodInvokeExpression) =>
			{
				//Statement: var task = this.client.GetAsync(requestUri.ToString());
				method.Statements.Add(new CodeVariableDeclarationStatement(
					new CodeTypeReference("var"), "responseMessage", httpMethodInvokeExpression));

			};


			if (uriQueryParameters.Length > 0)
			{
				AddRequestUriWithQueryAssignmentStatement();
			}
			else
			{
				AddRequestUriAssignmentStatement();
			}

			if (singleFromBodyParameterDescription != null)
			{
				method.Statements.Add(new CodeSnippetStatement(
@"			using (var requestWriter = new System.IO.StringWriter())
			{
			var requestSerializer = JsonSerializer.Create();"
));
				method.Statements.Add(new CodeMethodInvokeExpression(new CodeSnippetExpression("requestSerializer"), "Serialize",
					new CodeSnippetExpression("requestWriter"),
					new CodeSnippetExpression(singleFromBodyParameterDescription.ParameterDescriptor.ParameterName)));


				method.Statements.Add(new CodeSnippetStatement(
@"			var content = new StringContent(requestWriter.ToString(), System.Text.Encoding.UTF8, ""application/json"");"
					));

				if (forAsync)
				{
					AddPostStatement(
					new CodeMethodInvokeExpression(new CodeSnippetExpression("await " + sharedContext.clientReference.FieldName), isPost ?
					"PostAsync" : "PutAsync", new CodeSnippetExpression("requestUri")
			  , new CodeSnippetExpression("content")));
				}
				else
				{
					AddPostStatement(new CodePropertyReferenceExpression(
					new CodeMethodInvokeExpression(sharedContext.clientReference, isPost ?
					"PostAsync" : "PutAsync", new CodeSnippetExpression("requestUri")
			  , new CodeSnippetExpression("content"))
					, "Result"));
				}
			}
			else
			{
				if (forAsync)
				{
					AddPostStatement(
						new CodeMethodInvokeExpression(new CodeSnippetExpression("await " + sharedContext.clientReference.FieldName), isPost ? "PostAsync" : "PutAsync"
						, new CodeSnippetExpression("requestUri")
						, new CodeSnippetExpression("new StringContent(String.Empty)")));
				}
				else
				{
					AddPostStatement(new CodePropertyReferenceExpression(
						new CodeMethodInvokeExpression(sharedContext.clientReference, isPost ? "PostAsync" : "PutAsync"
						, new CodeSnippetExpression("requestUri")
						, new CodeSnippetExpression("new StringContent(String.Empty)"))
						, "Result"));
				}

			}

			var resultReference = new CodeVariableReferenceExpression("responseMessage");

			//Statement: result.EnsureSuccessStatusCode();
			method.Statements.Add(new CodeMethodInvokeExpression(resultReference, "EnsureSuccessStatusCode"));

			//Statement: return something;
			if (returnType != null)
			{
				AddReturnStatement();
			}

			if (singleFromBodyParameterDescription != null)
				method.Statements.Add(new CodeSnippetStatement("\t\t\t}"));
		}

		static string RemoveTrialEmptyString(string s)
		{
			var p = s.IndexOf("+\"\"");
			if (p == -1)
			{
				return s;
			}
			return s.Remove(p, 3);
		}

	}

}
