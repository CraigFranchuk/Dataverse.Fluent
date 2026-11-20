namespace Dataverse.Fluent.Schema;

/// <summary>
/// The codes of the findings <see cref="DataverseSchemaValidator"/> reports. Pass one to
/// <see cref="SchemaValidationOptions.IgnoreCodes"/> to leave those findings out.
/// </summary>
public static class SchemaCodes
{
    /// <summary>DVS001 (error): the model's table does not exist, or no table has its entity set.</summary>
    public const string TableNotFound = "DVS001";

    /// <summary>DVS002 (error): the model's entity set is not its table's. Fixable.</summary>
    public const string EntitySetMismatch = "DVS002";

    /// <summary>DVS003 (error): the model's mapping attributes are invalid, so the client cannot use it.</summary>
    public const string InvalidMapping = "DVS003";

    /// <summary>DVS005 (info): the live <c>$select</c> check could not run for the model.</summary>
    public const string SelectCheckSkipped = "DVS005";

    /// <summary>DVS010 (error): the column does not exist on the table.</summary>
    public const string ColumnNotFound = "DVS010";

    /// <summary>DVS011 (error): the property's type cannot hold the column's values, or Dataverse rejects it on write.</summary>
    public const string IncompatibleType = "DVS011";

    /// <summary>DVS012 (warning): the property's type can lose part of the column's values.</summary>
    public const string LossyType = "DVS012";

    /// <summary>DVS013 (error): Dataverse rejects the column in a <c>$select</c>.</summary>
    public const string RejectedBySelect = "DVS013";

    /// <summary>DVS014 (warning): an enum member's value is not an option of the choice column.</summary>
    public const string EnumValueNotAnOption = "DVS014";

    /// <summary>DVS015 (info): an option of the choice column has no enum member.</summary>
    public const string OptionWithoutEnumMember = "DVS015";

    /// <summary>
    /// DVS016 (error): the column is the display name or label of another column (such as <c>createdbyname</c>), which
    /// the Web API returns as an annotation, not as a property.
    /// </summary>
    public const string CompanionColumn = "DVS016";

    /// <summary>DVS020 (error): the lookup column does not exist, or is not a lookup.</summary>
    public const string LookupNotFound = "DVS020";

    /// <summary>DVS021 (error): the lookup's navigation property is not the one Dataverse binds it with. Fixable.</summary>
    public const string LookupNavigationPropertyMismatch = "DVS021";

    /// <summary>DVS022 (error): the lookup's target entity set is not the entity set of the table it points at. Fixable
    /// when the lookup has one table.</summary>
    public const string LookupTargetEntitySetMismatch = "DVS022";

    /// <summary>DVS023 (warning): a single-table lookup is mapped on a column that can point at several tables.</summary>
    public const string PolymorphicColumnMappedAsSingleTable = "DVS023";

    /// <summary>DVS024 (warning): a polymorphic lookup is missing a table the column can point at. Fixable.</summary>
    public const string MissingLookupTarget = "DVS024";

    /// <summary>DVS025 (error): a polymorphic lookup maps a table the column cannot point at.</summary>
    public const string UnknownLookupTarget = "DVS025";

    /// <summary>DVS026 (error): a polymorphic lookup target's entity set or navigation property is wrong. Fixable.</summary>
    public const string LookupTargetMismatch = "DVS026";

    /// <summary>
    /// DVS027 (error): a lookup column is mapped as a plain column under its logical name. Dataverse returns it as
    /// <c>_{column}_value</c>, so the property is never filled.
    /// </summary>
    public const string LookupMappedAsColumn = "DVS027";

    /// <summary>
    /// DVS028 (error): a lookup mapped by its value name (<c>_{column}_value</c>) is written under that name, which
    /// Dataverse rejects with HTTP 400. Fixable.
    /// </summary>
    public const string LookupValueWritten = "DVS028";

    /// <summary>DVS030 (error): the collection navigation property does not exist on the table.</summary>
    public const string CollectionNavigationNotFound = "DVS030";

    /// <summary>DVS031 (error): the single-valued navigation property does not exist on the table.</summary>
    public const string NavigationNotFound = "DVS031";

    /// <summary>DVS032 (warning): the related record's type maps a different table than the relationship leads to.</summary>
    public const string RelatedTypeMismatch = "DVS032";

    /// <summary>
    /// DVS040 (warning): the column cannot be updated, but <c>SetFrom</c> sends it on update, where Dataverse ignores
    /// it. Fixable.
    /// </summary>
    public const string SentOnUpdate = "DVS040";

    /// <summary>
    /// DVS041 (warning): the column cannot be written at all, but <c>SetFrom</c> sends it, where Dataverse ignores it.
    /// Fixable.
    /// </summary>
    public const string NotWritable = "DVS041";

    /// <summary>DVS042 (info): <c>SetFrom</c> sends an annotation property, which Dataverse ignores. Fixable.</summary>
    public const string AnnotationWritten = "DVS042";
}
