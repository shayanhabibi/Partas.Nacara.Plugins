namespace Nacara.Plugins

open System
open System.Globalization

/// <summary>
/// Springs, solved the way anime.js 4.5.0 solves them, so that a spring written to CSS as
/// <c>linear()</c> and the same spring run by the bundled anime.js settle alike.
/// </summary>
module Spring =

    // anime.js constants (core/consts.js and easings/spring/index.js).
    let private minValue = 1e-11
    let private k = 1e3
    let private maxParameter = k * 10.0
    let private timeStep = 0.02
    let private restThreshold = 0.0005
    let private restDuration = 200.0
    let private maxDuration = 60000.0
    let private maxRestSteps = restDuration / timeStep / k
    let private maxIterations = maxDuration / timeStep / k

    let private clamp (low: float) (high: float) (value: float) : float = Math.Min(Math.Max(value, low), high)

    /// The solver's derived values: w0, zeta, wd and b of anime.js.
    type private Solver = { W0: float; Zeta: float; Wd: float; B: float }

    let private derive (spring: SpringParameters) =
        let mass = Math.Max(spring.Mass, minValue)
        let stiffness = clamp minValue maxParameter spring.Stiffness
        let damping = clamp minValue maxParameter spring.Damping
        let w0 = clamp minValue k (sqrt (stiffness / mass))
        let zeta = damping / (2.0 * sqrt (stiffness * mass))

        if zeta < 1.0 then
            let wd = w0 * sqrt (1.0 - zeta * zeta)
            { W0 = w0; Zeta = zeta; Wd = wd; B = zeta * w0 / wd }
        elif zeta = 1.0 then
            { W0 = w0; Zeta = zeta; Wd = 0.0; B = w0 }
        else
            let wd = w0 * sqrt (zeta * zeta - 1.0)
            { W0 = w0; Zeta = zeta; Wd = wd; B = zeta * w0 / wd }

    // anime solve(t) is 1 - displacement; this is the displacement.
    let private displacement (solver: Solver) (time: float) =
        if solver.Zeta < 1.0 then
            exp (-time * solver.Zeta * solver.W0)
            * (cos (solver.Wd * time) + solver.B * sin (solver.Wd * time))
        elif solver.Zeta = 1.0 then
            (1.0 + solver.B * time) * exp (-time * solver.W0)
        else
            ((1.0 + solver.B) * exp ((-solver.Zeta * solver.W0 + solver.Wd) * time)
             + (1.0 - solver.B) * exp ((-solver.Zeta * solver.W0 - solver.Wd) * time))
            / 2.0

    /// <summary>The position of the spring after <c>seconds</c>, from 0 at rest to 1 at the target.</summary>
    /// <remarks>This is anime.js's <c>solve(t)</c>, which is itself <c>1 - displacement</c>; a spring that overshoots exceeds 1.</remarks>
    let position (spring: SpringParameters) (seconds: float) : float =
        1.0 - displacement (derive spring) seconds

    /// <summary>The seconds the solver runs before the spring has rested, as anime.js measures it.</summary>
    /// <remarks>It steps 0.02s at a time and stops on the 11th consecutive step within 0.0005 of the target.</remarks>
    let solverDuration (spring: SpringParameters) : float =
        let solver = derive spring
        let mutable solverTime = 0.0
        let mutable restSteps = 0.0
        let mutable iterations = 0.0
        let mutable duration = 0.0

        while restSteps <= maxRestSteps && iterations <= maxIterations do
            if abs (displacement solver solverTime) < restThreshold then
                restSteps <- restSteps + 1.0
            else
                restSteps <- 0.0

            duration <- solverTime
            solverTime <- solverTime + timeStep
            iterations <- iterations + 1.0

        duration

    /// <summary>The milliseconds a spring takes to settle.</summary>
    /// <remarks>This is <c>round(solverDuration * 1000)</c>, anime.js's <c>settlingDuration</c>.</remarks>
    let settle (spring: SpringParameters) : int =
        int (Math.Round(solverDuration spring * k, MidpointRounding.AwayFromZero))

    let private samples = 32

    /// <summary>The spring as a CSS <c>linear()</c> easing of 32 samples over its settling time.</summary>
    /// <remarks>Each sample is rounded to 3 decimals. The first is <c>0</c> and the last is <c>1</c>.</remarks>
    let linear (spring: SpringParameters) : string =
        let solver = derive spring
        let duration = solverDuration spring

        let format (value: float) =
            (Math.Round(value, 3, MidpointRounding.AwayFromZero) + 0.0).ToString("0.###", CultureInfo.InvariantCulture)

        let sample index =
            if index = 0 then "0"
            elif index = samples - 1 then "1"
            else format (1.0 - displacement solver (duration * float index / float (samples - 1)))

        "linear(" + String.Join(", ", Array.init samples sample) + ")"

    /// <summary>The stand-in spring for one of Figma's named spring presets, or <c>None</c> for any other name.</summary>
    /// <remarks>Figma does not publish the parameters of <c>GENTLE</c>, <c>QUICK</c>, <c>BOUNCY</c> and <c>SLOW</c>; this is the one place that chooses them.</remarks>
    let preset (name: string) : SpringParameters option =
        match name with
        | "GENTLE" -> Some { Mass = 1.0; Stiffness = 100.0; Damping = 15.0 }
        | "QUICK" -> Some { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }
        | "BOUNCY" -> Some { Mass = 1.0; Stiffness = 600.0; Damping = 15.0 }
        | "SLOW" -> Some { Mass = 1.0; Stiffness = 80.0; Damping = 20.0 }
        | _ -> None

    /// <summary>The spring rewritten for anime.js, which clamps mass to at least 1.</summary>
    /// <remarks>A mass below 1 is rescaled to mass 1 by dividing stiffness and damping by it, which keeps the natural frequency and damping ratio, so the motion is unchanged.</remarks>
    let forAnime (spring: SpringParameters) : SpringParameters =
        if spring.Mass >= 1.0 then
            spring
        else
            { Mass = 1.0
              Stiffness = spring.Stiffness / spring.Mass
              Damping = spring.Damping / spring.Mass }
