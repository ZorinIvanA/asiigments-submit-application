using System.Text;
using LabsApp.Domain;

namespace LabsApp.Tests.Domain;

/// <summary>
/// Юнит-проверки LenientBodyReader (ADR-014, T-002): три состояния поля
/// НЕ схлопываются (String / JsonNull / Invalid); битый/пустой текст — все поля
/// Invalid + ParseSuccess=false, корень-не-объект — поля Invalid при ParseSuccess=true
/// (синтаксически валидный JSON); исключение парсинга не покидает ридер; проекция
/// auth-семейства GetStringOrNull коллапсирует JsonNull и Invalid в null; полная
/// проекция сохраняет различие (нужно PUT /students/{id}/group, FR-020).
/// </summary>
public sealed class LenientBodyReaderTests
{
    // ------------------------------------------------------------------
    // Критерий «Три состояния LenientField» (AC ISS-001)
    // ------------------------------------------------------------------

    [Fact]
    public void GroupId_JsonString_IsStringWithExactValue()
    {
        var field = new LenientBodyReader("""{"groupId":"X"}""").GetField("groupId");

        Assert.Equal(LenientFieldKind.String, field.Kind);
        Assert.Equal("X", field.Value);
    }

    [Fact]
    public void GroupId_LiteralNull_IsJsonNull()
    {
        var field = new LenientBodyReader("""{"groupId":null}""").GetField("groupId");

        Assert.Equal(LenientFieldKind.JsonNull, field.Kind);
        Assert.Null(field.Value);
    }

    [Fact]
    public void GroupId_Missing_IsInvalid()
    {
        var field = new LenientBodyReader("{}").GetField("groupId");

        Assert.Equal(LenientFieldKind.Invalid, field.Kind);
        Assert.Null(field.Value);
    }

    [Fact]
    public void GroupId_NonStringValues_AreInvalid()
    {
        // Матрица нестроковых значений: число / bool / объект / массив — все Invalid.
        foreach (var body in new[]
                 {
                     """{"groupId":123}""",
                     """{"groupId":-1}""",
                     """{"groupId":true}""",
                     """{"groupId":false}""",
                     """{"groupId":{"id":"g1"}}""",
                     """{"groupId":[1,2]}""",
                 })
        {
            Assert.Equal(LenientFieldKind.Invalid, new LenientBodyReader(body).GetField("groupId").Kind);
        }
    }

    [Fact]
    public void States_DoNotCollapse()
    {
        // Ключевой инвариант ISS-001: String ≠ JsonNull ≠ Invalid.
        var fromString = new LenientBodyReader("""{"groupId":"X"}""").GetField("groupId");
        var fromNull = new LenientBodyReader("""{"groupId":null}""").GetField("groupId");
        var fromMissing = new LenientBodyReader("{}").GetField("groupId");
        var fromNumber = new LenientBodyReader("""{"groupId":123}""").GetField("groupId");

        Assert.NotEqual(fromString.Kind, fromNull.Kind);
        Assert.NotEqual(fromNull.Kind, fromMissing.Kind);
        Assert.NotEqual(fromNull.Kind, fromNumber.Kind);
        Assert.NotEqual(fromString.Kind, fromNumber.Kind);
    }

    // ------------------------------------------------------------------
    // Критерий «Битый JSON»: исключение парсинга не покидает ридер,
    // ParseSuccess=false (ADR-014)
    // ------------------------------------------------------------------

    [Fact]
    public void BrokenJson_ParseSuccessFalse_AllFieldsInvalid()
    {
        var reader = new LenientBodyReader("{bad json");

        Assert.False(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("login").Kind);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("password").Kind);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("groupId").Kind);
    }

    [Theory]
    [InlineData("{oops")]
    [InlineData("{")]
    [InlineData("{\"groupId\":}")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void BrokenOrEmptyBody_ParseSuccessFalse(string? body)
    {
        Assert.False(new LenientBodyReader(body).ParseSuccess);
    }

    [Fact]
    public void Constructor_BrokenJson_DoesNotThrow()
    {
        var exception = Record.Exception(() => new LenientBodyReader("{oops"));

        Assert.Null(exception);
    }

    // ------------------------------------------------------------------
    // Критерий «Валидный JSON»: ParseSuccess=true (ADR-014 — граница веток
    // FR-007: битый JSON → 400, валидный JSON с отсутствующими полями → 401)
    // ------------------------------------------------------------------

    [Fact]
    public void StringValue_ParseSuccessTrue()
    {
        var reader = new LenientBodyReader("""{"a":"текст"}""");

        Assert.True(reader.ParseSuccess);
        var field = reader.GetField("a");
        Assert.Equal(LenientFieldKind.String, field.Kind);
        Assert.Equal("текст", field.Value);
    }

    [Fact]
    public void JsonNullValue_ParseSuccessTrue_KindIsJsonNull()
    {
        var reader = new LenientBodyReader("""{"a":null}""");

        Assert.True(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.JsonNull, reader.GetField("a").Kind);
        Assert.Null(reader.GetField("a").Value);
    }

    [Fact]
    public void NonStringValue_ParseSuccessTrue_KindIsInvalid()
    {
        var reader = new LenientBodyReader("""{"a":123}""");

        Assert.True(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("a").Kind);
        Assert.Null(reader.GetField("a").Value);
    }

    [Fact]
    public void EmptyObject_ParseSuccessTrue_FieldsInvalid()
    {
        var reader = new LenientBodyReader("{}");

        Assert.True(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("a").Kind);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"строка\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    public void NonObjectRoot_ValidJson_ParseSuccessTrue_NoFields(string body)
    {
        // Корень-не-объект — синтаксически ВАЛИДНЫЙ JSON (FR-023: 400 положен только
        // некорректному JSON), полей у него нет — все поля Invalid.
        var reader = new LenientBodyReader(body);

        Assert.True(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("login").Kind);
    }

    // ------------------------------------------------------------------
    // Критерий «Проекция auth-семейства» (FR-013/FR-017)
    // ------------------------------------------------------------------

    [Fact]
    public void GetStringOrNull_AuthMatrix_CollapsesJsonNullAndInvalid()
    {
        Assert.Equal("a", new LenientBodyReader("""{"login":"a"}""").GetStringOrNull("login"));
        Assert.Null(new LenientBodyReader("""{"login":null}""").GetStringOrNull("login"));
        Assert.Null(new LenientBodyReader("{}").GetStringOrNull("login"));
        Assert.Null(new LenientBodyReader("{oops").GetStringOrNull("login"));
    }

    [Fact]
    public void GetStringOrNull_PasswordField_SameProjection()
    {
        var reader = new LenientBodyReader("""{"login":"a","password":"пароль"}""");

        Assert.Equal("a", reader.GetStringOrNull("login"));
        Assert.Equal("пароль", reader.GetStringOrNull("password"));
    }

    [Fact]
    public void GetStringOrNull_NonStringLoginValue_Null()
    {
        Assert.Null(new LenientBodyReader("""{"login":42}""").GetStringOrNull("login"));
    }

    [Fact]
    public void GetStringOrNull_ValuePassesWithoutTrim()
    {
        // Трим — забота доменного валидатора, не ридера: значение проходит дословно.
        Assert.Equal("  a  ", new LenientBodyReader("""{"login":"  a  "}""").GetStringOrNull("login"));
    }

    // ------------------------------------------------------------------
    // Прочие контракты ридера
    // ------------------------------------------------------------------

    [Fact]
    public void MultipleFields_AreReadIndependently()
    {
        var reader = new LenientBodyReader("""{"login":"a","password":null,"code":5}""");

        Assert.Equal(LenientFieldKind.String, reader.GetField("login").Kind);
        Assert.Equal(LenientFieldKind.JsonNull, reader.GetField("password").Kind);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("code").Kind);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("отсутствует").Kind);
    }

    [Fact]
    public async Task FromStreamAsync_ReadsUtf8Body()
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("""{"login":"логин"}"""));

        var reader = await LenientBodyReader.FromStreamAsync(body);

        Assert.Equal("логин", reader.GetStringOrNull("login"));
    }

    [Fact]
    public async Task FromStreamAsync_EmptyStream_AllFieldsInvalid_ParseSuccessFalse()
    {
        using var body = new MemoryStream();

        var reader = await LenientBodyReader.FromStreamAsync(body);

        Assert.False(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.Invalid, reader.GetField("login").Kind);
    }

    [Fact]
    public async Task FromStreamAsync_ObjectBody_ParseSuccessTrue()
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":null}"""));

        var reader = await LenientBodyReader.FromStreamAsync(body);

        Assert.True(reader.ParseSuccess);
        Assert.Equal(LenientFieldKind.JsonNull, reader.GetField("a").Kind);
    }
}
