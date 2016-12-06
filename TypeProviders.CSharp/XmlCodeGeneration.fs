module TypeProviders.CSharp.XmlCodeGeneration

open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open TypeProviders.CSharp.CodeGeneration

let private xElementType = SyntaxFactory.ParseTypeName "System.Xml.Linq.XElement"

let private childElementsExpression =
    SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Elements")
    >> SF.methodInvocation []

let private attributesExpression =
    SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Attributes")
    >> SF.methodInvocation []

let private findFirstWithName nameExpr list =
    SyntaxFactory.ParseTypeName "System.Linq.Enumerable"
    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "FirstOrDefault")
    |> SF.methodInvocation
        [
            list |> SyntaxFactory.Argument

            SyntaxFactory
                .SimpleLambdaExpression(
                    SF.parameterWithoutType "p",
                    SyntaxFactory.IdentifierName "p"
                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Name")
                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "LocalName")
                    |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Equals")
                    |> SF.methodInvocation
                        [
                            nameExpr |> SyntaxFactory.Argument
                                                
                            SyntaxFactory.ParseTypeName "System.StringComparison"
                            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "InvariantCultureIgnoreCase")
                            |> SyntaxFactory.Argument
                        ]
                )
            |> SyntaxFactory.Argument
        ]

let private findFirstElementWithName xElementExpr propertyNameExpr =
    [
        childElementsExpression xElementExpr
        |> findFirstWithName propertyNameExpr
        |> SF.variableDeclaration "childElement"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

        SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.EqualsExpression,
                SyntaxFactory.IdentifierName "childElement",
                SyntaxFactory.LiteralExpression SyntaxKind.NullLiteralExpression
            ),
            SyntaxFactory.ParseTypeName "System.ArgumentException"
            |> SF.objectCreation [
                SyntaxFactory
                    .InterpolatedStringExpression(SyntaxKind.InterpolatedStringStartToken |> SyntaxFactory.Token)
                    .WithContents(
                        [
                            SF.interpolatedStringText "Unable to parse XML data. Element "
                            :> InterpolatedStringContentSyntax

                            SyntaxFactory.Interpolation xElementExpr
                            :> InterpolatedStringContentSyntax

                            SF.interpolatedStringText " doesn't have a child element named \\\""
                            :> InterpolatedStringContentSyntax

                            SyntaxFactory.Interpolation propertyNameExpr
                            :> InterpolatedStringContentSyntax

                            SF.interpolatedStringText "\\\""
                            :> InterpolatedStringContentSyntax
                        ]
                        |> SyntaxFactory.List
                    )
                |> SyntaxFactory.Argument
            ]
            |> SyntaxFactory.ThrowStatement
        )
        :> StatementSyntax

        SyntaxFactory.IdentifierName "childElement"
        |> SyntaxFactory.ReturnStatement
        :> StatementSyntax
    ]

let private findFirstElementOrAttributeValueWithName xElementExpr propertyNameExpr =
    [
        childElementsExpression xElementExpr
        |> findFirstWithName propertyNameExpr
        |> SF.variableDeclaration "childElement"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

        SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                SyntaxFactory.IdentifierName "childElement",
                SyntaxFactory.LiteralExpression SyntaxKind.NullLiteralExpression
            ),
            SyntaxFactory.IdentifierName "childElement"
            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Value")
            |> SyntaxFactory.ReturnStatement
        )
        :> StatementSyntax
        
        attributesExpression xElementExpr
        |> findFirstWithName propertyNameExpr
        |> SF.variableDeclaration "childAttribute"
        |> SyntaxFactory.LocalDeclarationStatement
        :> StatementSyntax

        SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                SyntaxFactory.IdentifierName "childAttribute",
                SyntaxFactory.LiteralExpression SyntaxKind.NullLiteralExpression
            ),
            SyntaxFactory.IdentifierName "childAttribute"
            |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Value")
            |> SyntaxFactory.ReturnStatement
        )
        :> StatementSyntax
        
        SyntaxFactory.ParseTypeName "System.ArgumentException"
        |> SF.objectCreation [
            SyntaxFactory
                .InterpolatedStringExpression(SyntaxKind.InterpolatedStringStartToken |> SyntaxFactory.Token)
                .WithContents(
                    [
                        SF.interpolatedStringText "Unable to parse XML data. Element "
                        :> InterpolatedStringContentSyntax

                        SyntaxFactory.Interpolation xElementExpr
                        :> InterpolatedStringContentSyntax

                        SF.interpolatedStringText " doesn't have an attribute or a child element named \\\""
                        :> InterpolatedStringContentSyntax

                        SyntaxFactory.Interpolation propertyNameExpr
                        :> InterpolatedStringContentSyntax

                        SF.interpolatedStringText "\\\""
                        :> InterpolatedStringContentSyntax
                    ]
                    |> SyntaxFactory.List
                )
            |> SyntaxFactory.Argument
        ]
        |> SyntaxFactory.ThrowStatement
        :> StatementSyntax
    ]

let rec getValueExpression expr = function
    | Predefined tChild ->
        expr
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Value")
        :> ExpressionSyntax
    | Generated _ -> expr :> ExpressionSyntax
    | Collection _ -> failwith "Nested collections are not possible in XML"
    | Optional t -> getValueExpression expr t
    | Existing _ -> failwith "Not implemented"

let rec private convertType propertyType (expr: ExpressionSyntax) =
    match propertyType with
    | Predefined t when t = TString -> expr
    | Predefined t ->
        CodeGeneration.getTypeSyntax propertyType
        |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Parse")
        |> SF.methodInvocation [ SyntaxFactory.Argument expr ]
        :> ExpressionSyntax
    | Generated t ->
        CodeGeneration.getTypeSyntax propertyType
        |> SF.objectCreation [ expr |> SyntaxFactory.Argument ]
        :> ExpressionSyntax
    | Collection t -> failwith "Nested collections are not possible in XML"
    | Optional t -> failwith "Nested optionals are not possible in XML"
    | Existing t -> failwith "Not implemented"

let rec private getPropertyAndConvertType propertyType propertyName (expr: ExpressionSyntax) =
    match propertyType with
    | Predefined t ->
        SyntaxFactory.IdentifierName "GetSimpleValue"
        |> SF.methodInvocation [
            expr |> SyntaxFactory.Argument
            SF.stringLiteral propertyName |> SyntaxFactory.Argument
        ]
        |> convertType propertyType
    | Generated t ->
        SyntaxFactory.IdentifierName "GetChildElement"
        |> SF.methodInvocation [
            expr |> SyntaxFactory.Argument
            SF.stringLiteral propertyName |> SyntaxFactory.Argument
        ]
        |> convertType propertyType
    | Collection t ->
        let valueExpression =
            getValueExpression (SyntaxFactory.IdentifierName "p") t

        SF.qualifiedTypeName [
            SyntaxFactory.IdentifierName "System"
            SyntaxFactory.IdentifierName "Collections"
            SyntaxFactory.IdentifierName "Generic"
            SF.genericName "List" [ CodeGeneration.getTypeSyntax t ]
        ]
        |> SF.objectCreation
            [
                SyntaxFactory.ParseTypeName "System.Linq.Enumerable"
                |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Select")
                |> SF.methodInvocation
                    [
                        childElementsExpression expr |> SyntaxFactory.Argument
                        
                        SyntaxFactory
                            .SimpleLambdaExpression(
                                SF.parameterWithoutType "p",
                                convertType t valueExpression
                            )
                        |> SyntaxFactory.Argument
                    ]
                |> SyntaxFactory.Argument
            ]
        :> ExpressionSyntax
    | Optional t ->
        getValueExpression expr t
        |> convertType t
    | Existing t -> failwith "Not implemented"

let generateDataStructure dataType =
    let generateAdditionalMembers (typeName: string) properties =
        [
            SyntaxFactory
                .ConstructorDeclaration(typeName)
                .WithModifiers([SyntaxKind.PublicKeyword] |> SF.getKeywordTokenList)
                .WithParameterList(
                    xElementType
                    |> SF.parameter "e"
                    |> SF.singletonParameter
                )
                .WithBody(
                    properties
                    |> List.map (fun (propertyName: string, propertyType) ->
                        SyntaxFactory
                            .AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName propertyName,
                                getPropertyAndConvertType propertyType propertyName (SyntaxFactory.IdentifierName "e")
                            )
                        |> SyntaxFactory.ExpressionStatement
                        :> StatementSyntax
                    )
                    |> SyntaxFactory.Block
                )
            :> MemberDeclarationSyntax

        ]
    CodeGeneration.generateDataStructure generateAdditionalMembers dataType

let generateCreationMethods dataType sampleData =
    let parseStreamStatements (returnType: TypeSyntax) (dataParam: ParameterSyntax) =
        [
            SF.objectCreation
                (xElementType
                |> SF.simpleMemberAccess (SyntaxFactory.IdentifierName "Load")
                |> SF.methodInvocation [ dataParam.Identifier |> SyntaxFactory.IdentifierName |> SyntaxFactory.Argument ]
                |> SyntaxFactory.Argument
                |> List.singleton)
                returnType
            |> SyntaxFactory.ReturnStatement
            :> StatementSyntax
        ]
    [
        yield! CodeGeneration.generateCreationMethods dataType sampleData parseStreamStatements "application/xml"
        yield
            SyntaxFactory
                .MethodDeclaration(
                    SF.stringType,
                    "GetSimpleValue"
                )
                .WithModifiers(
                    [
                        SyntaxKind.PrivateKeyword
                        SyntaxKind.StaticKeyword
                    ]
                    |> SF.getKeywordTokenList
                )
                .WithParameterList(
                    [
                        SF.parameter "e" xElementType
                        SF.parameter "propertyName" SF.stringType
                    ]
                    |> SyntaxFactory.SeparatedList
                    |> SyntaxFactory.ParameterList
                )
                .WithBody(
                    findFirstElementOrAttributeValueWithName
                        (SyntaxFactory.IdentifierName "e")
                        (SyntaxFactory.IdentifierName "propertyName")
                    |> SyntaxFactory.Block
                )
            :> MemberDeclarationSyntax

        yield
            SyntaxFactory
                .MethodDeclaration(
                    xElementType,
                    "GetChildElement"
                )
                .WithModifiers(
                    [
                        SyntaxKind.PrivateKeyword
                        SyntaxKind.StaticKeyword
                    ]
                    |> SF.getKeywordTokenList
                )
                .WithParameterList(
                    [
                        SF.parameter "e" xElementType
                        SF.parameter "propertyName" SF.stringType
                    ]
                    |> SyntaxFactory.SeparatedList
                    |> SyntaxFactory.ParameterList
                )
                .WithBody(
                    findFirstElementWithName
                        (SyntaxFactory.IdentifierName "e")
                        (SyntaxFactory.IdentifierName "propertyName")
                    |> SyntaxFactory.Block
                )
            :> MemberDeclarationSyntax
    ]
