﻿using System;
using System.CodeDom;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;

using System.Diagnostics;
using System.Text;
using Fonlow.TypeScriptCodeDom;
using Fonlow.Poco2Ts;
using Fonlow.Reflection;
using Fonlow.Web.Meta;

namespace Fonlow.CodeDom.Web.Ts
{
	/// <summary>
	/// Generate a client function upon ApiDescription for React + AXIOS
	/// </summary>
	public class ClientApiTsReactFunctionGen : ClientApiTsFunctionGenBase
	{
		const string ReactHttpResponse = "AxiosResponse";
		const string ReactHttpBlobResponse = "AxiosResponse<Blob>";
		const string ReactHttpStringResponse = "AxiosResponse<string>";

		string returnTypeText = null;
		string contentType;

		public ClientApiTsReactFunctionGen(string contentType) : base()
		{
			this.contentType = contentType;
		}

		protected override CodeMemberMethod CreateMethodName()
		{
			var returnTypeReference = Poco2TsGen.TranslateToClientTypeReference(ReturnType);
			returnTypeText = TypeMapper.MapCodeTypeReferenceToTsText(returnTypeReference);
			if (returnTypeText == "any" || returnTypeText == "void")
			{
				returnTypeText = ReactHttpResponse;
			}
			else if (returnTypeText == "response")
			{
				returnTypeText = ReactHttpStringResponse;
			}
			else if (returnTypeText == "blobresponse")
			{
				returnTypeText = ReactHttpBlobResponse;
			}

			var callbackTypeText = $"Promise<{returnTypeText}>";
			Debug.WriteLine("callback: " + callbackTypeText);
			var returnTypeReferenceWithObservable = new CodeSnipetTypeReference(callbackTypeText);

			return new CodeMemberMethod()
			{
				Attributes = MemberAttributes.Public | MemberAttributes.Final,
				Name = NethodName,
				ReturnType = returnTypeReferenceWithObservable,
			};
		}


		protected override void RenderImplementation()
		{
			var httpMethod = Description.HttpMethod.ToLower(); //Method is always uppercase.
															   //deal with parameters
			var parameters = Description.ParameterDescriptions.Select(d =>
				 new CodeParameterDeclarationExpression(Poco2TsGen.TranslateToClientTypeReference(d.ParameterDescriptor.ParameterType), d.Name)
			).ToList();

			//parameters.Add(new CodeParameterDeclarationExpression(callbackTypeReference, "callback"));

			Method.Parameters.AddRange(parameters.ToArray());

			var jsUriQuery = UriQueryHelper.CreateUriQueryForTs(Description.RelativePath, Description.ParameterDescriptions);
			var uriText = jsUriQuery == null ? $"this.baseUri + '{Description.RelativePath}'" :
				RemoveTrialEmptyString($"this.baseUri + '{jsUriQuery}'");

			// var mapFunction = returnTypeText == ReactHttpResponse ? String.Empty : ".map(response=> response.json())";

			if (ReturnType!=null && TypeHelper.IsStringType(ReturnType) && this.stringAsString)//stringAsString is for .NET Core Web API
			{
				if (httpMethod == "get" || httpMethod == "delete")
				{
					Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, {{ responseType: 'text' }});"));
					return;
				}

				if (httpMethod == "post" || httpMethod == "put")
				{
					var fromBodyParameterDescriptions = Description.ParameterDescriptions.Where(d => d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromBody
						|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && (!(d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
						|| (d.ParameterDescriptor.ParameterBinder == ParameterBinder.None)))).ToArray();
					if (fromBodyParameterDescriptions.Length > 1)
					{
						throw new InvalidOperationException(String.Format("This API function {0} has more than 1 FromBody bindings in parameters", Description.ActionDescriptor.ActionName));
					}
					var singleFromBodyParameterDescription = fromBodyParameterDescriptions.FirstOrDefault();

					var dataToPost = singleFromBodyParameterDescription == null ? "null" : singleFromBodyParameterDescription.ParameterDescriptor.ParameterName;

					if (String.IsNullOrEmpty(contentType))
					{
						contentType = "application/json;charset=UTF-8";
					}

					if (dataToPost == "null")
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, null, {{headers: {{ 'Content-Type': '{contentType}' }}, responseType: 'text' }});"));
					}
					else
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, JSON.stringify({dataToPost}), {{ headers: {{ 'Content-Type': '{contentType}' }}, responseType: 'text' }});"));
					}

					return;
				}

			}
			else if (returnTypeText == "AxiosResponse<Blob>")//translated from blobresponse to this
			{
				const string optionForStream = "{ responseType: 'blob' }";

				if (httpMethod == "get" || httpMethod == "delete")
				{
					Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, {optionForStream});"));
					return;
				}

				if (httpMethod == "post" || httpMethod == "put")
				{
					var fromBodyParameterDescriptions = Description.ParameterDescriptions.Where(d => d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromBody
						|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && (!(d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
						|| (d.ParameterDescriptor.ParameterBinder == ParameterBinder.None)))).ToArray();
					if (fromBodyParameterDescriptions.Length > 1)
					{
						throw new InvalidOperationException(String.Format("This API function {0} has more than 1 FromBody bindings in parameters", Description.ActionDescriptor.ActionName));
					}
					var singleFromBodyParameterDescription = fromBodyParameterDescriptions.FirstOrDefault();

					var dataToPost = singleFromBodyParameterDescription == null ? "null" : singleFromBodyParameterDescription.ParameterDescriptor.ParameterName;

					//if (String.IsNullOrEmpty(contentType))
					//{
					//	contentType = "application/json;charset=UTF-8";
					//}

					if (dataToPost == "null")
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, null, {optionForStream});"));
					}
					else
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, JSON.stringify({dataToPost}), {optionForStream});"));
					}

					return;
				}

			}
			else if (returnTypeText == "AxiosResponse<string>")//translated from response to this
			{
				const string optionForActionResult = "{ responseType: 'text' }";

				if (httpMethod == "get" || httpMethod == "delete")
				{
					Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, {optionForActionResult});"));
					return;
				}

				if (httpMethod == "post" || httpMethod == "put")
				{
					var fromBodyParameterDescriptions = Description.ParameterDescriptions.Where(d => d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromBody
						|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && (!(d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
						|| (d.ParameterDescriptor.ParameterBinder == ParameterBinder.None)))).ToArray();
					if (fromBodyParameterDescriptions.Length > 1)
					{
						throw new InvalidOperationException(String.Format("This API function {0} has more than 1 FromBody bindings in parameters", Description.ActionDescriptor.ActionName));
					}
					var singleFromBodyParameterDescription = fromBodyParameterDescriptions.FirstOrDefault();

					var dataToPost = singleFromBodyParameterDescription == null ? "null" : singleFromBodyParameterDescription.ParameterDescriptor.ParameterName;

					if (dataToPost == "null")
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, null, {optionForActionResult});"));
					}
					else
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, JSON.stringify({dataToPost}), {{ headers: {{ 'Content-Type': '{contentType}' }}, responseType: 'text' }});"));
					}

					return;
				}

			}
			else
			{
				if (httpMethod == "get" || httpMethod == "delete")
				{
					Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText});"));
					return;
				}

				if (httpMethod == "post" || httpMethod == "put")
				{
					var fromBodyParameterDescriptions = Description.ParameterDescriptions.Where(d => d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromBody
						|| (TypeHelper.IsComplexType(d.ParameterDescriptor.ParameterType) && (!(d.ParameterDescriptor.ParameterBinder == ParameterBinder.FromUri)
						|| (d.ParameterDescriptor.ParameterBinder == ParameterBinder.None)))).ToArray();
					if (fromBodyParameterDescriptions.Length > 1)
					{
						throw new InvalidOperationException(String.Format("This API function {0} has more than 1 FromBody bindings in parameters", Description.ActionDescriptor.ActionName));
					}
					var singleFromBodyParameterDescription = fromBodyParameterDescriptions.FirstOrDefault();

					var dataToPost = singleFromBodyParameterDescription == null ? "null" : singleFromBodyParameterDescription.ParameterDescriptor.ParameterName;

					if (String.IsNullOrEmpty(contentType))
					{
						contentType = "application/json;charset=UTF-8";
					}

					if (dataToPost == "null")
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, null, {{ headers: {{ 'Content-Type': '{contentType}' }} }});"));
					}
					else
					{
						Method.Statements.Add(new CodeSnippetStatement($"return Axios.{httpMethod}({uriText}, JSON.stringify({dataToPost}), {{ headers: {{ 'Content-Type': '{contentType}' }} }});"));
					}

					return;
				}
			}

			Debug.Assert(false, "How come?");
		}

	}

}