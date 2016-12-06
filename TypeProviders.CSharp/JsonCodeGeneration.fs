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
                                SyntaxFactory
                                    .LocalDeclarationStatement(
                                        SF.objectCreation
                                            (SyntaxFactory.ParseTypeName "Newtonsoft.Json.JsonSerializer")
                                            []
                                        |> SF.variableDeclaration "serializer"
                                    )
                                    :> StatementSyntax

                                SyntaxFactory
                                    .ReturnStatement(
                                        SF.methodInvocation
                                            (SF.simpleMemberAccess (SF.genericName "Deserialize" [ returnType ]) (SyntaxFactory.IdentifierName "serializer"))
                                            [ SyntaxFactory.IdentifierName "jsonTextReader" |> SyntaxFactory.Argument ]
                                            
                                    )
                                    :> StatementSyntax
                            ]
                            |> SyntaxFactory .Block
                        )
                        .WithDeclaration(
                            SF.objectCreation
                                (SyntaxFactory.ParseTypeName "Newtonsoft.Json.JsonTextReader")
                                [ SyntaxFactory.IdentifierName "textReader" |> SyntaxFactory.Argument ]
                            |> SF.variableDeclaration "jsonTextReader"
                        )
                )
                .WithDeclaration(
                    SF.objectCreation
                        (SyntaxFactory.ParseTypeName "System.IO.StreamReader")
                        [ dataParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                    |> SF.variableDeclaration "textReader"
                )
                :> StatementSyntax
        ]
    CodeGeneration.generateCreationMethods dataType sampleData parseStreamStatements "application/json"
