# Dependency Graph

```mermaid
flowchart LR
  subgraph Cluster0[Cluster 0]
    AppliesToListContentBlock["AppliesToListContentBlock<br/><small>service</small>"]
    ArticleSchemaTest["ArticleSchemaTest<br/><small>service</small>"]
    AuditRuleTest["AuditRuleTest<br/><small>service</small>"]
    CodeContentBlock["CodeContentBlock<br/><small>service</small>"]
    CodeParserService["CodeParserService<br/><small>service</small>"]
    ContentBlock["ContentBlock<br/><small>service</small>"]
    ContentHarness["ContentHarness<br/><small>service</small>"]
    ContentMatch["ContentMatch<br/><small>service</small>"]
    ContentMatchGroups["ContentMatchGroups<br/><small>model</small>"]
    ContentParameters["ContentParameters<br/><small>other</small>"]
    ContentParserService["ContentParserService<br/><small>service</small>"]
    ContentParserServiceBase["ContentParserServiceBase<br/><small>service</small>"]
    ContentRange["ContentRange<br/><small>model</small>"]
    DocIndexException["DocIndexException<br/><small>exception</small>"]
    ExternalFileReference["ExternalFileReference<br/><small>service</small>"]
    FileContentMatch["FileContentMatch<br/><small>service</small>"]
    FileContentPositionIndex["FileContentPositionIndex<br/><small>service</small>"]
    IndexParserService["IndexParserService<br/><small>service</small>"]
    LearnParserService["LearnParserService<br/><small>service</small>"]
    LinkContentBlock["LinkContentBlock<br/><small>service</small>"]
    LinterDiagnostic["LinterDiagnostic<br/><small>model</small>"]
    LintIndex["LintIndex<br/><small>service</small>"]
    ListContentBlock["ListContentBlock<br/><small>service</small>"]
    ListItemContentBlock["ListItemContentBlock<br/><small>service</small>"]
    MarkdownHarness["MarkdownHarness<br/><small>service</small>"]
    MarkdownParserService["MarkdownParserService<br/><small>service</small>"]
    MetadataContentBlock["MetadataContentBlock<br/><small>service</small>"]
    MetadataFieldContentBlock["MetadataFieldContentBlock<br/><small>service</small>"]
    NoteContentBlock["NoteContentBlock<br/><small>service</small>"]
    ParagraphContentBlock["ParagraphContentBlock<br/><small>service</small>"]
    RepositoryParserInstance["RepositoryParserInstance<br/><small>service</small>"]
    SchemaError["SchemaError<br/><small>other</small>"]
    ToCParserService["ToCParserService<br/><small>service</small>"]
    TripleColonContentBlock["TripleColonContentBlock<br/><small>service</small>"]
  end
  subgraph Cluster1[Cluster 1]
    PomChildValidationError["PomChildValidationError<br/><small>service</small>"]
    PomValidationError["PomValidationError<br/><small>other</small>"]
  end
  subgraph Cluster5[Cluster 5]
    LinterExtension["LinterExtension<br/><small>service</small>"]
  end
  JsonEnum["JsonEnum<br/><small>model</small>"]
  ContentLine["ContentLine<br/><small>other</small>"]
  CodeParserService -.->|inject| IRepoBase
  CodeParserService -.->|inject| ILogger
  CodeParserService -.->|inject| IAppSettings
  CodeParserService -.->|inject| IRepoLoader
  CodeParserService -.->|inject| IContentParserOptions
  CodeParserService -->|concrete| Branch
  CodeParserService ==>|inherits| ContentParserServiceBase
  CodeParserService -.->|impl| IContentParser
  ContentParserServiceBase -.->|inject| IRepoBase
  ContentParserServiceBase -.->|inject| ILogger
  ContentParserServiceBase -.->|inject| IAppSettings
  ContentParserServiceBase -.->|inject| IRepoLoader
  ContentParserServiceBase -.->|inject| IContentParserOptions
  ContentParserServiceBase -->|concrete| Branch
  ContentParserServiceBase ==>|inherits| ParserServiceBase
  ContentParserServiceBase -.->|impl| IContentParser
  ContentParserService -->|concrete| ContentParserService
  ContentParserService -.->|inject| IRepoBase
  ContentParserService -.->|inject| ILogger
  ContentParserService -.->|inject| IAppSettings
  ContentParserService -.->|inject| IRepoLoader
  ContentParserService -.->|inject| IContentParserOptions
  ContentParserService -->|concrete| Branch
  ContentParserService ==>|inherits| LearnParserService
  ContentParserService -.->|impl| IContentParser
  IndexParserService -.->|inject| IRepoBase
  IndexParserService -.->|inject| ILogger
  IndexParserService -.->|inject| IAppSettings
  IndexParserService -.->|inject| IRepoLoader
  IndexParserService -.->|inject| IContentParserOptions
  IndexParserService -->|concrete| Branch
  IndexParserService ==>|inherits| CodeParserService
  IndexParserService -.->|impl| IContentParser
  LearnParserService -.->|inject| IRepoBase
  LearnParserService -.->|inject| ILogger
  LearnParserService -.->|inject| IAppSettings
  LearnParserService -.->|inject| IRepoLoader
  LearnParserService -.->|inject| IContentParserOptions
  LearnParserService -->|concrete| Branch
  LearnParserService ==>|inherits| MarkdownParserService
  LearnParserService -.->|impl| IContentParser
  MarkdownParserService -.->|inject| IRepoBase
  MarkdownParserService -.->|inject| ILogger
  MarkdownParserService -.->|inject| IAppSettings
  MarkdownParserService -.->|inject| IRepoLoader
  MarkdownParserService -.->|inject| IContentParserOptions
  MarkdownParserService -->|concrete| Branch
  MarkdownParserService ==>|inherits| ToCParserService
  MarkdownParserService -.->|impl| IContentParser
  ToCParserService -.->|inject| IRepoBase
  ToCParserService -.->|inject| ILogger
  ToCParserService -.->|inject| IAppSettings
  ToCParserService -.->|inject| IRepoLoader
  ToCParserService -.->|inject| IContentParserOptions
  ToCParserService -->|concrete| Branch
  ToCParserService ==>|inherits| IndexParserService
  ToCParserService -.->|impl| IContentParser
  ExternalFileReference -->|concrete| ExternalFileReferenceTypeEnum
  ExternalFileReference -.->|inject| IJobOutputInstance
  ExternalFileReference -->|concrete| FileContentMatch
  ExternalFileReference -->|concrete| List_IContentBlock_
  ExternalFileReference -->|concrete| ExternalFileReference
  ExternalFileReference -->|concrete| Nullable_int_
  ExternalFileReference ==>|inherits| RelatedFile
  ExternalFileReference -.->|impl| IRelatedFile
  ExternalFileReference -.->|impl| IExternalFileReference
  ExternalFileReference -.->|impl| IRangeReference
  ContentLine -->|concrete| ContentMatch
  ContentLine ==>|inherits| ContentPositionIndex
  ContentLine -.->|impl| IContentPositionIndex
  ContentMatch -->|concrete| int
  ContentMatch -.->|inject| IContentPositionIndex
  ContentMatch -->|concrete| Match
  ContentMatch -->|concrete| ContentMatch
  ContentMatch -->|concrete| FileContentMatch
  ContentMatch -->|concrete| Regex
  ContentMatch -->|concrete| TripleColonBlock
  ContentMatch -->|concrete| InclusionBlock
  ContentMatch -->|concrete| CodeSnippet
  ContentMatch -->|concrete| TripleColonInline
  ContentMatch -->|concrete| SourceSpan
  ContentMatch -->|concrete| Dictionary_string__string_
  ContentMatch -->|concrete| InclusionInline
  ContentMatch ==>|inherits| ContentPositionIndex
  ContentMatch -.->|impl| IContentPositionIndex
  ContentMatch -.->|impl| IContentMatch
  ContentMatch -.->|impl| IContentMatchGroups
  ContentMatchGroups -->|concrete| ContentMatchGroups
  ContentMatchGroups -->|concrete| Regex
  ContentMatchGroups -->|concrete| TripleColonBlock
  ContentMatchGroups -->|concrete| InclusionBlock
  ContentMatchGroups -->|concrete| CodeSnippet
  ContentMatchGroups -->|concrete| TripleColonInline
  ContentMatchGroups -->|concrete| Dictionary_string__string_
  ContentMatchGroups -->|concrete| InclusionInline
  FileContentMatch -->|concrete| int
  FileContentMatch -.->|inject| IContentBase
  FileContentMatch -.->|inject| IFileContentPositionIndex
  FileContentMatch -->|concrete| Match
  FileContentMatch -->|concrete| FileContentMatch
  FileContentMatch -->|concrete| Regex
  FileContentMatch -->|concrete| TripleColonBlock
  FileContentMatch -->|concrete| InclusionBlock
  FileContentMatch -->|concrete| CodeSnippet
  FileContentMatch -->|concrete| TripleColonInline
  FileContentMatch -->|concrete| SourceSpan
  FileContentMatch -->|concrete| Dictionary_string__string_
  FileContentMatch -->|concrete| InclusionInline
  FileContentMatch -->|concrete| ContentMatch
  FileContentMatch ==>|inherits| FileContentPositionIndex
  FileContentMatch -.->|impl| IContentPositionIndex
  FileContentMatch -.->|impl| IFileContentPositionIndex
  FileContentMatch -.->|impl| IRangeReference
  FileContentMatch -.->|impl| IFileContentMatch
  FileContentMatch -.->|impl| IContentMatch
  FileContentMatch -.->|impl| IContentMatchGroups
  ContentHarness -.->|inject| IContentParserOptions
  ContentHarness -.->|inject| IJobOutputInstance
  ContentHarness -->|concrete| CancellationToken
  ContentHarness -.->|inject| ILogger
  ContentHarness -.->|inject| IContentBase
  ContentHarness ==>|inherits| ContentParameters
  MarkdownHarness -->|concrete| MarkdownParserService
  MarkdownHarness -.->|inject| IJobOutputInstance
  MarkdownHarness -->|concrete| CancellationToken
  MarkdownHarness ==>|inherits| ContentHarness
  ContentParameters -->|concrete| string
  ContentParameters -->|concrete| int
  ContentParameters -->|concrete| ContentParameters
  ContentParameters -->|concrete| ContentHarness
  ContentRange -->|concrete| ContentRange
  ContentRange -->|concrete| int
  ContentRange -.->|inject| IContentBase
  ContentRange -.->|inject| IContentPositionIndex
  ContentRange -->|concrete| Block
  ContentRange -->|concrete| ContentLine
  ContentRange -->|concrete| ContentLinePosition
  ContentRange -->|concrete| List_ContentLine_
  AppliesToListContentBlock -->|concrete| ParagraphBlock
  AppliesToListContentBlock -.->|inject| IDocFxContentBlockBuilder
  AppliesToListContentBlock -->|concrete| LiteralInline
  AppliesToListContentBlock -->|concrete| int
  AppliesToListContentBlock -->|concrete| string
  AppliesToListContentBlock -->|concrete| ContentParameters
  AppliesToListContentBlock -.->|inject| IContentBlock
  AppliesToListContentBlock -.->|inject| IExternalFileReference
  AppliesToListContentBlock -->|concrete| Nullable_int_
  AppliesToListContentBlock ==>|inherits| ContentBlock
  AppliesToListContentBlock -.->|impl| IContentPositionIndex
  AppliesToListContentBlock -.->|impl| IFileContentPositionIndex
  AppliesToListContentBlock -.->|impl| IRangeReference
  AppliesToListContentBlock -.->|impl| IContentBlock
  AppliesToListContentBlock -.->|impl| IWriteMyselfBase
  AppliesToListContentBlock -.->|impl| IBlockContainer
  CodeContentBlock -->|concrete| FileContentMatch
  CodeContentBlock -->|concrete| string
  CodeContentBlock -->|concrete| ContentParameters
  CodeContentBlock -->|concrete| int
  CodeContentBlock -->|concrete| FencedCodeBlock
  CodeContentBlock -.->|inject| IDocFxContentBlockBuilder
  CodeContentBlock -->|concrete| CodeBlock
  CodeContentBlock -.->|inject| IContentBlock
  CodeContentBlock -.->|inject| IExternalFileReference
  CodeContentBlock -->|concrete| Nullable_int_
  CodeContentBlock ==>|inherits| ContentBlock
  CodeContentBlock -.->|impl| IContentPositionIndex
  CodeContentBlock -.->|impl| IFileContentPositionIndex
  CodeContentBlock -.->|impl| IRangeReference
  CodeContentBlock -.->|impl| IContentBlock
  CodeContentBlock -.->|impl| IWriteMyselfBase
  CodeContentBlock -.->|impl| IBlockContainer
  ContentBlock -->|concrete| ArtifactEnum
  ContentBlock -->|concrete| FileContentMatch
  ContentBlock -->|concrete| ContentParameters
  ContentBlock -->|concrete| int
  ContentBlock -->|concrete| string
  ContentBlock -.->|inject| IContentBase
  ContentBlock -->|concrete| Block
  ContentBlock -.->|inject| IDocFxContentBlockBuilder
  ContentBlock -->|concrete| Inline
  ContentBlock -->|concrete| SourceSpan
  ContentBlock -->|concrete| ContentLine
  ContentBlock -.->|inject| IContentBlock
  ContentBlock -.->|inject| IExternalFileReference
  ContentBlock -->|concrete| Nullable_int_
  ContentBlock ==>|inherits| FileContentPositionIndex
  ContentBlock -.->|impl| IContentPositionIndex
  ContentBlock -.->|impl| IFileContentPositionIndex
  ContentBlock -.->|impl| IRangeReference
  ContentBlock -.->|impl| IContentBlock
  ContentBlock -.->|impl| IWriteMyselfBase
  ContentBlock -.->|impl| IBlockContainer
  FileContentPositionIndex -->|concrete| int
  FileContentPositionIndex -.->|inject| IContentBase
  FileContentPositionIndex -->|concrete| Match
  FileContentPositionIndex -.->|inject| IFileContentPositionIndex
  FileContentPositionIndex -.->|inject| IContentPositionIndex
  FileContentPositionIndex -->|concrete| ContentRange
  FileContentPositionIndex -->|concrete| ContentLine
  FileContentPositionIndex -->|concrete| SourceSpan
  FileContentPositionIndex ==>|inherits| ContentPositionIndex
  FileContentPositionIndex -.->|impl| IContentPositionIndex
  FileContentPositionIndex -.->|impl| IFileContentPositionIndex
  FileContentPositionIndex -.->|impl| IRangeReference
  LinkContentBlock -->|concrete| LinkInline
  LinkContentBlock -.->|inject| IDocFxContentBlockBuilder
  LinkContentBlock -->|concrete| int
  LinkContentBlock -->|concrete| string
  LinkContentBlock -->|concrete| LinkTypeEnum
  LinkContentBlock -.->|inject| IContentBase
  LinkContentBlock -->|concrete| ContentParameters
  LinkContentBlock -->|concrete| FileContentMatch
  LinkContentBlock -.->|inject| IContentBlock
  LinkContentBlock -.->|inject| IExternalFileReference
  LinkContentBlock -->|concrete| TripleColonInline
  LinkContentBlock -->|concrete| TripleColonBlock
  LinkContentBlock -->|concrete| Nullable_int_
  LinkContentBlock ==>|inherits| ContentBlock
  LinkContentBlock -.->|impl| IContentPositionIndex
  LinkContentBlock -.->|impl| IFileContentPositionIndex
  LinkContentBlock -.->|impl| IRangeReference
  LinkContentBlock -.->|impl| IContentBlock
  LinkContentBlock -.->|impl| IWriteMyselfBase
  LinkContentBlock -.->|impl| IBlockContainer
  ListContentBlock -->|concrete| ListBlock
  ListContentBlock -.->|inject| IDocFxContentBlockBuilder
  ListContentBlock -->|concrete| ArtifactEnum
  ListContentBlock -->|concrete| FileContentMatch
  ListContentBlock -->|concrete| ContentParameters
  ListContentBlock -.->|inject| IContentBlock
  ListContentBlock -.->|inject| IExternalFileReference
  ListContentBlock -->|concrete| Nullable_int_
  ListContentBlock ==>|inherits| ContentBlock
  ListContentBlock -.->|impl| IContentPositionIndex
  ListContentBlock -.->|impl| IFileContentPositionIndex
  ListContentBlock -.->|impl| IRangeReference
  ListContentBlock -.->|impl| IContentBlock
  ListContentBlock -.->|impl| IWriteMyselfBase
  ListContentBlock -.->|impl| IBlockContainer
  ListItemContentBlock -->|concrete| ArtifactEnum
  ListItemContentBlock -->|concrete| FileContentMatch
  ListItemContentBlock -->|concrete| ContentParameters
  ListItemContentBlock -->|concrete| ListItemBlock
  ListItemContentBlock -.->|inject| IDocFxContentBlockBuilder
  ListItemContentBlock -.->|inject| IContentBlock
  ListItemContentBlock -.->|inject| IExternalFileReference
  ListItemContentBlock -->|concrete| Nullable_int_
  ListItemContentBlock ==>|inherits| ContentBlock
  ListItemContentBlock -.->|impl| IContentPositionIndex
  ListItemContentBlock -.->|impl| IFileContentPositionIndex
  ListItemContentBlock -.->|impl| IRangeReference
  ListItemContentBlock -.->|impl| IContentBlock
  ListItemContentBlock -.->|impl| IWriteMyselfBase
  ListItemContentBlock -.->|impl| IBlockContainer
  MetadataContentBlock -->|concrete| ContentParameters
  MetadataContentBlock -.->|inject| ILogger
  MetadataContentBlock -.->|inject| IContentBlock
  MetadataContentBlock -.->|inject| IExternalFileReference
  MetadataContentBlock -->|concrete| MetadataContentBlock
  MetadataContentBlock -->|concrete| Nullable_int_
  MetadataContentBlock ==>|inherits| ContentBlock
  MetadataContentBlock -.->|impl| IContentPositionIndex
  MetadataContentBlock -.->|impl| IFileContentPositionIndex
  MetadataContentBlock -.->|impl| IRangeReference
  MetadataContentBlock -.->|impl| IContentBlock
  MetadataContentBlock -.->|impl| IWriteMyselfBase
  MetadataContentBlock -.->|impl| IBlockContainer
  MetadataFieldContentBlock -->|concrete| ArtifactAttributeEnum
  MetadataFieldContentBlock -->|concrete| string
  MetadataFieldContentBlock -->|concrete| int
  MetadataFieldContentBlock -->|concrete| ContentParameters
  MetadataFieldContentBlock -->|concrete| List_string_
  MetadataFieldContentBlock -.->|inject| IContentBlock
  MetadataFieldContentBlock -.->|inject| IExternalFileReference
  MetadataFieldContentBlock -->|concrete| Nullable_int_
  MetadataFieldContentBlock ==>|inherits| ContentBlock
  MetadataFieldContentBlock -.->|impl| IContentPositionIndex
  MetadataFieldContentBlock -.->|impl| IFileContentPositionIndex
  MetadataFieldContentBlock -.->|impl| IRangeReference
  MetadataFieldContentBlock -.->|impl| IContentBlock
  MetadataFieldContentBlock -.->|impl| IWriteMyselfBase
  MetadataFieldContentBlock -.->|impl| IBlockContainer
  NoteContentBlock -->|concrete| QuoteSectionNoteBlock
  NoteContentBlock -.->|inject| IDocFxContentBlockBuilder
  NoteContentBlock -->|concrete| FileContentMatch
  NoteContentBlock -->|concrete| ContentParameters
  NoteContentBlock -.->|inject| IContentBlock
  NoteContentBlock -.->|inject| IExternalFileReference
  NoteContentBlock -->|concrete| Nullable_int_
  NoteContentBlock ==>|inherits| ContentBlock
  NoteContentBlock -.->|impl| IContentPositionIndex
  NoteContentBlock -.->|impl| IFileContentPositionIndex
  NoteContentBlock -.->|impl| IRangeReference
  NoteContentBlock -.->|impl| IContentBlock
  NoteContentBlock -.->|impl| IWriteMyselfBase
  NoteContentBlock -.->|impl| IBlockContainer
  ParagraphContentBlock -->|concrete| ParagraphBlock
  ParagraphContentBlock -.->|inject| IDocFxContentBlockBuilder
  ParagraphContentBlock -->|concrete| LiteralInline
  ParagraphContentBlock -->|concrete| int
  ParagraphContentBlock -->|concrete| string
  ParagraphContentBlock -->|concrete| ContentParameters
  ParagraphContentBlock -.->|inject| IContentBlock
  ParagraphContentBlock -.->|inject| IExternalFileReference
  ParagraphContentBlock -->|concrete| Nullable_int_
  ParagraphContentBlock ==>|inherits| ContentBlock
  ParagraphContentBlock -.->|impl| IContentPositionIndex
  ParagraphContentBlock -.->|impl| IFileContentPositionIndex
  ParagraphContentBlock -.->|impl| IRangeReference
  ParagraphContentBlock -.->|impl| IContentBlock
  ParagraphContentBlock -.->|impl| IWriteMyselfBase
  ParagraphContentBlock -.->|impl| IBlockContainer
  TripleColonContentBlock -->|concrete| CodeSnippet
  TripleColonContentBlock -.->|inject| IDocFxContentBlockBuilder
  TripleColonContentBlock -->|concrete| TripleColonBlock
  TripleColonContentBlock -->|concrete| TripleColonInline
  TripleColonContentBlock -.->|inject| IContentBlock
  TripleColonContentBlock -.->|inject| IExternalFileReference
  TripleColonContentBlock -->|concrete| Nullable_int_
  TripleColonContentBlock ==>|inherits| ContentBlock
  TripleColonContentBlock -.->|impl| IContentPositionIndex
  TripleColonContentBlock -.->|impl| IFileContentPositionIndex
  TripleColonContentBlock -.->|impl| IRangeReference
  TripleColonContentBlock -.->|impl| IContentBlock
  TripleColonContentBlock -.->|impl| IWriteMyselfBase
  TripleColonContentBlock -.->|impl| IBlockContainer
  RepositoryParserInstance -.->|inject| IRepositoryParserService
  RepositoryParserInstance -.->|inject| IRepoBase
  RepositoryParserInstance -->|concrete| RestClient
  RepositoryParserInstance -.->|inject| IRepositoryParserOptions
  RepositoryParserInstance -.->|inject| IRepoLoader
  RepositoryParserInstance -.->|inject| ILogger
  RepositoryParserInstance -.->|inject| IAppSettings
  RepositoryParserInstance ==>|inherits| ServiceInstanceBase
  RepositoryParserInstance -.->|impl| IRepositoryParserInstance
  RepositoryParserInstance -.->|impl| IRepoScanStatus
  LinterDiagnostic -->|concrete| SchemaError
  LinterDiagnostic -->|concrete| FullTextDocument
  LinterDiagnostic -->|concrete| DiagnosticSeverity
  LinterDiagnostic -->|concrete| Range
  LinterDiagnostic -->|concrete| string
  LinterDiagnostic -->|concrete| List_DiagnosticRelatedInformation_
  LinterDiagnostic -->|concrete| LintingExtraData
  LinterDiagnostic -->|concrete| Uri
  LinterDiagnostic -->|concrete| Diagnostic
  LinterDiagnostic -.->|inject| IContentBlock
  LinterDiagnostic -.->|inject| ILogger
  LinterDiagnostic ==>|inherits| Diagnostic
  LinterDiagnostic -.->|impl| ICanHaveData
  LinterExtension -.->|inject| ILanguageServerFacade
  LinterExtension -->|concrete| ForegroundThreadManager
  LinterExtension -.->|inject| ILanguageServerConfiguration
  LinterExtension -->|concrete| VSCodeOptions
  LinterExtension -->|concrete| RepoLoader
  LinterExtension -->|concrete| HoverLock
  LinterExtension -->|concrete| RestClient
  DocIndexException -->|concrete| string
  DocIndexException -->|concrete| int
  DocIndexException -->|concrete| Exception
  DocIndexException ==>|inherits| Exception
  JsonEnum -->|concrete| List_string_
  LintIndex -.->|inject| IAuditable
  LintIndex -->|concrete| ContentRange
  LintIndex -.->|inject| ILintIndex
  LintIndex -->|concrete| int
  LintIndex -->|concrete| string
  LintIndex -->|concrete| bool
  LintIndex -.->|inject| IContentBlock
  LintIndex -.->|inject| IContentBase
  LintIndex -.->|inject| ILintingPlaceholder
  LintIndex -.->|impl| ILintIndex
  SchemaError -->|concrete| ValidationError
  SchemaError -->|concrete| string
  SchemaError -->|concrete| NodeEvent
  SchemaError -->|concrete| Exception
  SchemaError -->|concrete| ParsingEvent
  SchemaError -->|concrete| YamlException
  SchemaError -->|concrete| JsonSerializationException
  SchemaError ==>|inherits| DocIndexException
  SchemaError -.->|impl| ISchemaError
  PomValidationError -->|concrete| AuditEntryBuilder
  PomValidationError -->|concrete| ValidationErrorKind
  PomValidationError -->|concrete| string
  PomValidationError -->|concrete| JToken
  PomValidationError -->|concrete| JsonSchema
  PomValidationError -->|concrete| DocfxError
  PomValidationError -->|concrete| PomValidationError
  PomValidationError ==>|inherits| ValidationError
  PomChildValidationError -->|concrete| AuditEntryBuilder
  PomChildValidationError -->|concrete| ValidationErrorKind
  PomChildValidationError -->|concrete| string
  PomChildValidationError -.->|inject| IReadOnlyDictionary_JsonSchema__ICollection_ValidationError__
  PomChildValidationError -->|concrete| JToken
  PomChildValidationError -->|concrete| JsonSchema
  PomChildValidationError -->|concrete| DocfxError
  PomChildValidationError ==>|inherits| ChildSchemaValidationError
  ArticleSchemaTest -.->|inject| IJobOutputInstance
  ArticleSchemaTest -->|concrete| AuditorService
  ArticleSchemaTest -->|concrete| ContentObjectSchema
  ArticleSchemaTest -.->|inject| IBlockContainer
  ArticleSchemaTest -->|concrete| CancellationToken
  ArticleSchemaTest -.->|inject| ILogger
  ArticleSchemaTest -->|concrete| ArticleSchemaTestHaness
  ArticleSchemaTest -->|concrete| string
  ArticleSchemaTest -->|concrete| ArticleSchemaTest
  ArticleSchemaTest -.->|impl| IAuditSchemaTest
  ArticleSchemaTest -.->|impl| IBlockContainer
  AuditRuleTest -->|concrete| AuditRuleTestHarness
  AuditRuleTest -->|concrete| string
  AuditRuleTest -->|concrete| AuditRuleTest
  AuditRuleTest -.->|inject| IAuditRule
  AuditRuleTest -.->|inject| IAuditRuleTest
  AuditRuleTest -.->|inject| ITestResult
  AuditRuleTest -.->|inject| IContentBlock
  AuditRuleTest -.->|inject| IExternalFileReference
  AuditRuleTest -.->|impl| IAuditRuleTest
  style Cluster0 fill:#f5f5f5,stroke:#999,stroke-width:2px
  style Cluster1 fill:#f5f5f5,stroke:#999,stroke-width:2px
  style Cluster5 fill:#f5f5f5,stroke:#999,stroke-width:2px

```
