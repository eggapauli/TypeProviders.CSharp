module TypeProviders.CSharp.JsonCodeGeneration

open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open TypeProviders.CSharp.CodeGeneration

let generateCreationMethods dataType sampleData =
    let parseStreamStatements (returnType: TypeSyntax) (dataParam: ParameterSyntax) =
        [
            SyntaxFactory
                .UsingStatement(
                    SyntaxFactory
                        .UsingStatement(
                            [
                                SyntaxFactory.ParseTypeName "Newtonsoft.Json.JsonSerializer"
                                |> SF.objectCreation []
                                |> SF.variableDeclaration "serializer"
                                |> SyntaxFactory.LocalDeclarationStatement
                                :> StatementSyntax

                                SyntaxFactory.IdentifierName "serializer"
                                |> SF.simpleMemberAccess (SF.genericName "Deserialize" [ returnType ])
                                |> SF.methodInvocation [ SyntaxFactory.IdentifierName "jsonTextReader" |> SyntaxFactory.Argument ]
                                |> SyntaxFactory.ReturnStatement
                                :> StatementSyntax
                            ]
                            |> SyntaxFactory .Block
                        )
                        .WithDeclaration(
                            SyntaxFactory.ParseTypeName "Newtonsoft.Json.JsonTextReader"
                            |> SF.objectCreation [ SyntaxFactory.IdentifierName "textReader" |> SyntaxFactory.Argument ]
                            |> SF.variableDeclaration "jsonTextReader"
                        )
                )
                .WithDeclaration(
                    SyntaxFactory.ParseTypeName "System.IO.StreamReader"
                    |> SF.objectCreation [ dataParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                    |> SF.variableDeclaration "textReader"
                )
                :> StatementSyntax
        ]
    CodeGeneration.generateCreationMethods dataType sampleData parseStreamStatements "application/json"
