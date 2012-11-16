// Copyright (c) AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;

namespace RenameClass
{
	class Program
	{
        static List<object> searchedMembers;
        static List<dynamic> refs;
        static string memberName;
        static string keywordName;

		public static void Main(string[] args)
		{
            if (args.Length != 4)
            {
                Console.WriteLine("use: RenameClass.exe <SolutionPath> <ClassNamespace> <CurrentClassName> <NewClassName>");
                return;
            }

            var solutionFile = args[0]; // "C:\\Users\\v-ezeqs\\Documents\\Visual Studio 2010\\Projects\\Application36\\Application36.sln"
            var classNamespace = args[1]; // "Application36.WebHost"
            var className = args[2]; // "SiteMaster"
            var classNewName = args[3]; // "SiteMaster2"

            if (!File.Exists(solutionFile))
            {
                Console.WriteLine("Solution not found at {0}", solutionFile);
                return;
            }

            Console.WriteLine("Loading solution...");
            
            // Loading Solution in Memory
            Solution solution = new Solution(solutionFile);


            Console.WriteLine("Finding references...");

            // Define which Type I'm looking for
            var typeReference = new GetClassTypeReference(classNamespace, className) as ITypeReference;
            
            // Try to find the Type definition in solution's projects 
            foreach (var proj in solution.Projects)
	        {
                var type = typeReference.Resolve(proj.Compilation);
                if (type.Kind != TypeKind.Unknown)
                {
                    SetSearchedMembers(new List<object>() { type });
                }
	        }

            if (searchedMembers == null)
            {
                Console.WriteLine("Not References found. Refactoring Done.");
                return;
            }

            // Find all related members related with the Type (like Members, Methods, etc)
            ICSharpCode.NRefactory.CSharp.Resolver.FindReferences refFinder = new ICSharpCode.NRefactory.CSharp.Resolver.FindReferences();
            var scopes = searchedMembers.Select (e => refFinder.GetSearchScopes (e as IEntity));
            
            // Finding references to the Type on the one of the different Solution files
            refs = new List<dynamic>();
            foreach (var file in solution.AllFiles) {
                foreach (var scope in scopes)
                {
                    refFinder.FindReferencesInFile(
                        scope,
                        file.UnresolvedTypeSystemForFile,
                        file.SyntaxTree,
                        file.Project.Compilation,
                        (astNode, result) =>
                        {
                            var newRef = GetReference(result, astNode, file);
                            if (newRef == null || refs.Any(r => r.File.FileName == newRef.File.FileName && r.Region == newRef.Region))
                                return;
                            refs.Add(newRef);
                        },
                        CancellationToken.None
                    );
                }
			}

			Console.WriteLine("Refactoring {0} places in {1} files...",
                              refs.Count(),
			                  refs.Select(x => x.File.FileName).Distinct().Count());
			
			
            // Perform replace for each of the References found
            foreach (var r in refs) {
				// DocumentScript expects the the AST to stay unmodified (so that it fits
				// to the document state at the time of the DocumentScript constructor call),
				// so we call Freeze() to prevent accidental modifications (e.g. forgetting a Clone() call).
                r.File.SyntaxTree.Freeze();

				// Create a document containing the file content:
				var document = new StringBuilderDocument(r.File.OriginalText);
				using (var script = new DocumentScript(document, FormattingOptionsFactory.CreateAllman(), new TextEditorOptions())) {
                    // Alternative 1: clone a portion of the AST and modify it
                    //var copy = (InvocationExpression)expr.Clone();
                    //copy.Arguments.Add(stringComparisonAst.Member("Ordinal"));
                    //script.Replace(expr, copy);
							
                    // Alternative 2: perform direct text insertion / replace
                    int offset = script.GetCurrentOffset(r.Region.Begin);
                    var length = r.Region.End.Column - r.Region.Begin.Column;

                    script.Replace(offset, length, classNewName);
				}
				File.WriteAllText(r.File.FileName, document.Text);
			}

            Console.WriteLine("Refactoring Done.");
		}

        public static void SetSearchedMembers(IEnumerable<object> members)
        {
            searchedMembers = new List<object>(members);
            var firstMember = searchedMembers.FirstOrDefault();
            if (firstMember is INamedElement)
            {
                var namedElement = (INamedElement)firstMember;
                var name = namedElement.Name;
                if (namedElement is IMethod && (((IMethod)namedElement).IsConstructor | ((IMethod)namedElement).IsDestructor))
                    name = ((IMethod)namedElement).DeclaringType.Name;
                memberName = name;

                keywordName = CSharpAmbienceNetToCSharpTypeName(namedElement.FullName);
                if (keywordName == namedElement.FullName)
                    keywordName = null;
            }
            if (firstMember is string)
                memberName = firstMember.ToString();
            if (firstMember is IVariable)
                memberName = ((IVariable)firstMember).Name;
            if (firstMember is ITypeParameter)
                memberName = ((ITypeParameter)firstMember).Name;
        }

        private static string CSharpAmbienceNetToCSharpTypeName(string netTypeName)
        {
            Dictionary<string, string> netToCSharpTypes = new Dictionary<string, string> ();

            netToCSharpTypes["System.Void"] = "void";
            netToCSharpTypes["System.Object"] = "object";
            netToCSharpTypes["System.Boolean"] = "bool";
            netToCSharpTypes["System.Byte"] = "byte";
            netToCSharpTypes["System.SByte"] = "sbyte";
            netToCSharpTypes["System.Char"] = "char";
            netToCSharpTypes["System.Enum"] = "enum";
            netToCSharpTypes["System.Int16"] = "short";
            netToCSharpTypes["System.Int32"] = "int";
            netToCSharpTypes["System.Int64"] = "long";
            netToCSharpTypes["System.UInt16"] = "ushort";
            netToCSharpTypes["System.UInt32"] = "uint";
            netToCSharpTypes["System.UInt64"] = "ulong";
            netToCSharpTypes["System.Single"] = "float";
            netToCSharpTypes["System.Double"] = "double";
            netToCSharpTypes["System.Decimal"] = "decimal";
            netToCSharpTypes["System.String"] = "string";

            if (netToCSharpTypes.ContainsKey(netTypeName))
                return netToCSharpTypes[netTypeName];
            return netTypeName;
        }


        static dynamic GetReference(ResolveResult result, AstNode node, CSharpFile file)
        {
            if (result == null)
            {
                return null;
            }

            object valid = null;
            if (result is MethodGroupResolveResult)
            {
                valid = ((MethodGroupResolveResult)result).Methods.FirstOrDefault(
                    m => searchedMembers.Any(member => member is IMethod && ((IMethod)member).Region == m.Region));
            }
            else if (result is MemberResolveResult)
            {
                var foundMember = ((MemberResolveResult)result).Member;
                valid = searchedMembers.FirstOrDefault(
                    member => member is IMember && ((IMember)member).Region == foundMember.Region);
            }
            else if (result is NamespaceResolveResult)
            {
                var ns = ((NamespaceResolveResult)result).NamespaceName;
                valid = searchedMembers.FirstOrDefault(n => n is string && n.ToString() == ns);
            }
            else if (result is LocalResolveResult)
            {
                var ns = ((LocalResolveResult)result).Variable;
                valid = searchedMembers.FirstOrDefault(n => n is IVariable && ((IVariable)n).Region == ns.Region);
            }
            else if (result is TypeResolveResult)
            {
                valid = searchedMembers.FirstOrDefault(n => n is IType);
            }
            else
            {
                valid = searchedMembers.FirstOrDefault();
            }
            if (node is ConstructorInitializer)
                return null;

            if (node is ObjectCreateExpression)
                node = ((ObjectCreateExpression)node).Type;

            if (node is InvocationExpression)
                node = ((InvocationExpression)node).Target;

            if (node is MemberReferenceExpression)
                node = ((MemberReferenceExpression)node).MemberNameToken;

            if (node is MemberType)
                node = ((MemberType)node).MemberNameToken;

            if (node is TypeDeclaration && (searchedMembers.First() is IType))
                node = ((TypeDeclaration)node).NameToken;
            if (node is DelegateDeclaration)
                node = ((DelegateDeclaration)node).NameToken;

            if (node is EntityDeclaration && (searchedMembers.First() is IMember))
                node = ((EntityDeclaration)node).NameToken;

            if (node is ParameterDeclaration && (searchedMembers.First() is IParameter))
                node = ((ParameterDeclaration)node).NameToken;
            if (node is ConstructorDeclaration)
                node = ((ConstructorDeclaration)node).NameToken;
            if (node is DestructorDeclaration)
                node = ((DestructorDeclaration)node).NameToken;
            if (node is NamedArgumentExpression)
                node = ((NamedArgumentExpression)node).NameToken;
            if (node is NamedExpression)
                node = ((NamedExpression)node).NameToken;
            if (node is VariableInitializer)
                node = ((VariableInitializer)node).NameToken;

            if (node is IdentifierExpression)
            {
                node = ((IdentifierExpression)node).IdentifierToken;
            }

            var region = new DomRegion(file.FileName, node.StartLocation, node.EndLocation);
            
            var length = node is PrimitiveType ? keywordName.Length : node.EndLocation.Column - node.StartLocation.Column;
            return new { valid = valid, Region = region, Length = length, File = file };
        }
	}
}