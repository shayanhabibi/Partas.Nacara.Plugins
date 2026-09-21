namespace Partas.Nacara.Theme

/// <summary>One named part of a theme's stylesheet.</summary>
/// <remarks>
/// Each part is written into a cascade layer of its own, <c>nacara.&lt;name&gt;</c>, in the order
/// the parts are listed. A layer can be replaced, dropped, or added beside the theme's own, which
/// is how a plugin styles a theme rather than fighting it: a stylesheet that declares
/// <c>@layer nacara, mine;</c> wins over every rule the theme writes, whatever its specificity.
/// </remarks>
type StyleLayer =

    {
        /// <summary>What the layer is called, which is also the name of its cascade layer.</summary>
        /// <remarks>Lowercase and dotless - <c>components</c> becomes <c>nacara.components</c>.</remarks>
        Name: string
        /// <summary>The rules, as they would be written in a stylesheet.</summary>
        Css: string
    }


/// <summary>
/// The cascade layers the theme of this build declares, for a plugin that has to order its own
/// against them.
/// </summary>
/// <remarks>
/// <para>
/// A CSS tool cannot ask the theme directly without depending on it, and a plugin cannot read what
/// the theme contributed to the registry unless it was registered after the theme - which the
/// theme cannot require, since it reads what plugins offered and so must come last itself. This
/// is written once while the theme configures itself and read when a stylesheet is bundled, by
/// which time every plugin has configured.
/// </para>
/// <para>
/// One build at a time, in one process. A host building two sites at once would see them share
/// this.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module ThemeLayers =

    let mutable private declared: string list = []

    /// <summary>The theme says what its layers are called, in cascade order.</summary>
    /// <param name="names">Their full names - <c>nacara.tokens</c>, <c>nacara.base</c>.</param>
    let declare (names: string list) = declared <- names

    /// <summary>What the theme declared, or nothing if no theme did.</summary>
    /// <remarks>
    /// Read it when bundling rather than when configuring: a plugin registered before the theme
    /// would otherwise see the empty list.
    /// </remarks>
    let current () = declared

/// <summary>Style layers plugins offered the theme.</summary>
/// <remarks>
/// A plugin with styling of its own puts it here and the theme bundles it, in one of the theme's
/// own layers or after them. A theme that never reads these is within its rights: an offer, not a
/// rule.
/// </remarks>
[<RequireQualifiedAccess>]
module PluginLayers =

    let mutable private offered: StyleLayer list = []

    /// <summary>Offer the theme a layer of your own.</summary>
    /// <param name="layer">What it is called and what it holds. The name is yours to choose; a
    /// name the theme already uses replaces nothing, so choose one it does not.</param>
    let offer (layer: StyleLayer) = offered <- offered @ [ layer ]

    /// <summary>Everything plugins offered, in the order they offered it.</summary>
    let all () = offered
