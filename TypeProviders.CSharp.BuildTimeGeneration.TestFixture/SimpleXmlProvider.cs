using TypeProviders.CSharp.BuildTimeGeneration.Attributes;

namespace TypeProviders.CSharp.BuildTimeGeneration.TestFixture
{
    [XmlProvider("<?xml version=\"1.0\"?><OrderedItem asd=\"qwe\" inventory:sdf=\"wer\" xmlns:inventory=\"http://www.cpandl.com\" xmlns:money=\"http://www.cohowinery.com\"><inventory:ItemName>Widget</inventory:ItemName><inventory:Description><short>Regular Widget</short><long>Regular Widget</long></inventory:Description><money:UnitPrice>2.3</money:UnitPrice><inventory:Quantity>10</inventory:Quantity><money:LineTotal>23</money:LineTotal></OrderedItem>")]
    public partial class SimpleXmlProvider
    {
    }
}
