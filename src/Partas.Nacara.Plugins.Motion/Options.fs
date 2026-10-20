namespace Nacara.Plugins

/// <summary>The engine that plays a hover.</summary>
type MotionEngine =
    /// <summary>CSS transitions, written into the site's stylesheet.</summary>
    | Css
    /// <summary>anime.js, run by a script on the page.</summary>
    | Anime

/// <summary>Where the anime.js library comes from.</summary>
type AnimeSource =
    /// <summary>The copy vendored in this package, written beside the site's output.</summary>
    | Bundled
    /// <summary>A module loaded from a url.</summary>
    | Cdn of url: string

/// <summary>What one hover is told to do beside the document.</summary>
type HoverSettings =
    {
        /// <summary>Whether the hover is written at all.</summary>
        /// <remarks>Defaults to <c>true</c>.</remarks>
        Enabled: bool
        /// <summary>The engine of this hover, instead of <c>MotionOptions.Engine</c>.</summary>
        /// <remarks>Defaults to <c>None</c>, which uses the plugin's engine.</remarks>
        Engine: MotionEngine option
        /// <summary>The duration in milliseconds, instead of the document's.</summary>
        /// <remarks>Defaults to <c>None</c>, which uses the document's duration.</remarks>
        Duration: int option
        /// <summary>The easing, instead of the document's.</summary>
        /// <remarks>Defaults to <c>None</c>, which uses the document's easing.</remarks>
        Easing: Easing option
        /// <summary>CSS values that replace a change's hovered value, by key.</summary>
        /// <remarks>
        /// A key is the property for the root element, such as <c>background-color</c>, or
        /// <c>Layer/property</c> for a part, such as <c>Accent/width</c>.
        /// Defaults to <c>Map.empty</c>.
        /// </remarks>
        Values: Map<string, string>
    }

/// <summary>Options of the motion plugin.</summary>
type MotionOptions =
    {
        /// <summary>The motion document the Figma plugin exported.</summary>
        /// <remarks>Resolved against the working directory. Defaults to <c>"design/tokens/motion.json"</c>.</remarks>
        Document: string
        /// <summary>The engine that plays the hovers.</summary>
        /// <remarks>Defaults to <c>Css</c>.</remarks>
        Engine: MotionEngine
        /// <summary>What the stylesheet does for visitors who ask for reduced motion.</summary>
        /// <remarks>Defaults to <c>Instant</c>.</remarks>
        ReducedMotion: ReducedMotion
        /// <summary>Where anime.js comes from, for hovers it plays.</summary>
        /// <remarks>Defaults to <c>Bundled</c>.</remarks>
        AnimeSource: AnimeSource
        /// <summary>Settings of single hovers, by hover name.</summary>
        /// <remarks>Defaults to <c>Map.empty</c>.</remarks>
        Hovers: Map<string, HoverSettings>
    }

/// <summary>The hovers of a document, split by the engine that plays them.</summary>
type CompiledMotion =
    {
        /// <summary>The stylesheet of the hovers that CSS plays.</summary>
        Css: string
        /// <summary>The hovers that anime.js plays.</summary>
        AnimeHovers: Hover list
        /// <summary>What the document leaves out, one line each.</summary>
        Warnings: string list
    }
