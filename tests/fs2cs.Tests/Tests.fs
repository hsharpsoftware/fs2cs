module fs2cs.Tests

open System
open fs2cs
open NUnit.Framework
open Serilog
open Microsoft.FSharp.Compiler.SourceCodeServices
open Newtonsoft.Json
open FSharp.Reflection
open Newtonsoft.Json.Serialization
open System.IO

type ErasedUnionConverter() =
    inherit JsonConverter()
    override x.CanConvert t =
        t.Name = "FSharpOption`1" ||
        FSharpType.IsUnion t &&
            t.GetCustomAttributes true
            |> Seq.exists (fun a -> (a.GetType ()).Name = "EraseAttribute")
    override x.ReadJson(reader, t, v, serializer) =
        failwith "Not implemented"
    override x.WriteJson(writer, v, serializer) =
        match FSharpValue.GetUnionFields (v, v.GetType()) with
        | _, [|v|] -> serializer.Serialize(writer, v) 
        | _ -> writer.WriteNull()  
        
type CustomResolver() =
    inherit DefaultContractResolver()
    override x.CreateProperty(member1, memberSerialization)  =
      let property = base.CreateProperty(member1, memberSerialization)
      let badPropNames = 
        [| "FSharpDelegateSignature"; "QualifiedName"; "FullName"; "AbbreviatedType";
           "GenericParameter"; "GetterMethod"; "EventAddMethod"; "EventRemoveMethod";
           "EventDelegateType"; "EventIsStandard"; "SetterMethod"; "NamedEntity";
           "TypeDefinition"
        |]
      if badPropNames |> Seq.exists( fun p -> p = property.PropertyName ) then 
        property.ShouldSerialize <- ( fun _ -> false )
      property
      

let printCs fileName par =
    let file = new StreamWriter(fileName+".cs")
    file.WriteLine(par.ToString())
    file.Close()

let printJson fileName par =
    let jsonSettings = 
        JsonSerializerSettings(
            Converters=[|ErasedUnionConverter()|],
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            StringEscapeHandling=StringEscapeHandling.EscapeNonAscii)
    jsonSettings.ContractResolver <- CustomResolver()
    let result = JsonConvert.SerializeObject (par, jsonSettings)
    File.WriteAllText(fileName+".json", result )

[<SetUpFixture>]
type SetupTest() =
    [<SetUp>]
    let ``start logging`` =
        Log.Logger <- LoggerConfiguration()
            .Destructure.FSharpTypes()
            .MinimumLevel.Debug() 
            .WriteTo.File("log.txt")
            .CreateLogger()
        Log.Information "Logging started"

[<Test>]
let ``empty "()" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"()"|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = ( a.[0] |> snd ).ToString()
  Assert.AreEqual( sprintf "public class %s {\r\n    public static object Invoke() {\r\n        return null;\r\n    }\r\n}" ( a.[0] |> fst ).Root.Name, content )

[<Test>]
let ``simple "let a=12345" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"let a=12345"|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = (snd  a.[0]).ToString()
  let file = fst a.[0]
  Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int a = 12345;\r\n}" file.Root.Name, content )

[<Test>]
let ``simple "let a=12345;;let b=678" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"let a=12345;;let b=678"|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = (snd  a.[0]).ToString()
  let file = fst a.[0]
  Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int a = 12345;\r\n    public static readonly int b = 678;\r\n}" file.Root.Name, content )


[<Test>]
let ``simple "let c=\"hello\"" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"let c=\"hello\""|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = (snd  a.[0]).ToString()
  let file = fst a.[0]
  Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly string c = \"hello\";\r\n}" file.Root.Name, content )

[<Test>]
let ``simple "let a=12345;;let b=a+1" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"let a=12345;;let b=a+1"|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = (snd  a.[0]).ToString()
  let file = fst a.[0]
  Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int a = 12345;\r\n    public static readonly int b = a + 1;\r\n}" file.Root.Name, content )

[<Test>]
let ``simple "let a=\"Hello\";;let b=a+\" Dolly!\"" compile works`` () =
  let compiled = Library.compile |> Library.main [|"--code";"let a=\"Hello\";;let b=a+\" Dolly!\""|]
  Assert.NotNull(compiled)
  Assert.IsNotEmpty(compiled)
  let a = compiled |> Seq.toArray
  Assert.AreEqual( 1, a.Length )
  let content = (snd  a.[0]).ToString()
  let file = fst a.[0]
  Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly string a = \"Hello\";\r\n    public static readonly string b = a + \" Dolly!\";\r\n}" file.Root.Name, content )


(* 
[CompilationMapping(SourceConstructFlags.Module)]
public static class Test1
{
	public static int a
	{
		[DebuggerNonUserCode, CompilerGenerated]
		get
		{
			return 123;
		}
	}

	public static int fac(int b)
	{
		return b + 1;
	}
}
*)
[<Test>]
let ``simple "let a=123;;let fac b = b+1;;fac a" compile works`` () =
  //try
    let compiled = Library.compile |> Library.main [|"--code";"let a=123;;let fac b = b+1;;fac a"|]
    Assert.NotNull(compiled)
    Assert.IsNotEmpty(compiled)
    let a = compiled |> Seq.toArray
    Assert.AreEqual( 1, a.Length )
    let content = (snd  a.[0]).ToString()
    let file = fst a.[0]
    Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int a = 123;\r\n    public static int fac(int b) {\r\n        return b + 1;\r\n    }\r\n\r\n    public static int Invoke() {\r\n        return fac(a);\r\n    }\r\n}" file.Root.Name, content )
  (*with
  | ex ->
    if ex :? System.Reflection.ReflectionTypeLoadException then
      let loaderEx = ex :?> System.Reflection.ReflectionTypeLoadException
      failwith (loaderEx.LoaderExceptions |> Seq.fold ( fun acc elem -> acc + "\n" + elem.Message ) String.Empty)
    else
      failwith ex.Message*)

[<Test>]
let ``simple "let dummy _ = ()" compile works`` () =
  //try
    let compiled = Library.compile |> Library.main [|"--code";"let dummy _ = ()"|]
    Assert.NotNull(compiled)
    Assert.IsNotEmpty(compiled)
    let a = compiled |> Seq.toArray
    Assert.AreEqual( 1, a.Length )
    let content = (snd  a.[0]).ToString()
    let file = fst a.[0]
    Assert.AreEqual( sprintf "public class %s {\r\n    public static object dummy(object _arg1) {\r\n        return null;\r\n    }\r\n}" file.Root.Name, content )    

[<Test>]
let ``simple "let x = 10 + 12 - 3" compile works`` () =
  //try
    let compiled = Library.compile |> Library.main [|"--code";"let x = 10 + 12 - 3"|]
    Assert.NotNull(compiled)
    Assert.IsNotEmpty(compiled)
    let a = compiled |> Seq.toArray
    Assert.AreEqual( 1, a.Length )
    let content = (snd  a.[0]).ToString()
    let file = fst a.[0]
    Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int x = 10 + 12 - 3;\r\n}" file.Root.Name, content )

[<Test>]
let ``complex fsx test`` () =
  //try
    let compiled = Library.compile |> Library.main [|"--projFile";"../../test.fsx"|]
    Assert.NotNull(compiled)
    Assert.IsNotEmpty(compiled)
    let a = compiled |> Seq.toArray
    Assert.AreEqual( 1, a.Length )
    let content = (snd  a.[0]).ToString()
    let file = fst a.[0]
    Assert.AreEqual( sprintf "public class %s {\r\n    public static readonly int x = 10 + 12 - 3;\r\n}" file.Root.Name, content )
