﻿[<AutoOpen>]
module Fun.Build.ConditionsBuilder

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Spectre.Console
open Fun.Build.Internal
open Fun.Build.BuiltinCmdsInternal
open Fun.Build.StageContextExtensions
open Fun.Build.StageContextExtensionsInternal
open Fun.Build.PipelineContextExtensionsInternal


module Internal =

    type StageContext with

        member ctx.WhenEnvArg(info: EnvArg) =
            if info.Name |> String.IsNullOrEmpty then
                failwith "ENV variable name cannot be empty"

            let getResult () =
                info.IsOptional
                || (
                    match ctx.TryGetEnvVar info.Name with
                    | ValueSome v when info.Values.IsEmpty || List.contains v info.Values -> true
                    | _ -> false
                )

            let getPrintInfo (prefix: string) =
                makeCommandOption (prefix + "env: ") (makeEnvNameForPrint info) (defaultArg info.Description "" + makeValuesForPrint info.Values)

            match ctx.GetMode() with
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp cmdHelpCtx ->
                cmdHelpCtx.EnvArgs.Add info
                false
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                else
                    AnsiConsole.MarkupLineInterpolated($"[red]✕ {getPrintInfo (ctx.BuildIndent().Substring(2))}[/]")
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenEnvArg(name: string, ?argValue, ?description, ?isOptional) =
            let argValue = defaultArg argValue ""
            ctx.WhenEnvArg
                {
                    EnvArg.Name = name
                    Description = description
                    Values = [
                        if String.IsNullOrEmpty argValue |> not then argValue
                    ]
                    IsOptional = defaultArg isOptional false
                }


        member ctx.WhenCmdArg(info: CmdArg) =
            let mode = ctx.GetMode()
            if info.Name.Names |> Seq.filter (String.IsNullOrEmpty >> not) |> Seq.isEmpty then
                failwith "Cmd name cannot be empty"

            let getResult () =
                let isValueMatch (v: string) =
                    match ctx.TryGetCmdArg v with
                    | ValueSome v when info.Values.Length = 0 || List.contains v info.Values -> true
                    | _ -> false

                if info.IsOptional then true else info.Name.Names |> Seq.exists isValueMatch

            let getPrintInfo (prefix: string) =
                makeCommandOption (prefix + "cmd: ") (makeCmdNameForPrint mode info) (defaultArg info.Description "" + makeValuesForPrint info.Values)

            match mode with
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.WriteLine(getPrintInfo (ctx.BuildIndent()))
                false
            | Mode.CommandHelp cmdHelpCtx ->
                cmdHelpCtx.CmdArgs.Add info
                false
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated $"""[green]{getPrintInfo ("✓ " + ctx.BuildIndent().Substring(2))}[/]"""
                else
                    AnsiConsole.MarkupLineInterpolated $"""[red]{getPrintInfo ("✕ " + ctx.BuildIndent().Substring(2))}[/]"""
                false
            | Mode.Execution -> getResult ()


        member ctx.WhenCmdArg(name: CmdName, ?argValue: string, ?description, ?isOptional) =
            let argValue = defaultArg argValue ""
            ctx.WhenCmdArg
                {
                    CmdArg.Name = name
                    Description = description
                    Values = [
                        if String.IsNullOrEmpty argValue |> not then argValue
                    ]
                    IsOptional = defaultArg isOptional false
                }


        member ctx.WhenBranch(branches: string seq) =
            let getResult () =
                try
                    let command = ctx.BuildCommand("git branch --show-current")
                    ctx.GetWorkingDir() |> ValueOption.iter (fun x -> command.WorkingDirectory <- x)

                    command.RedirectStandardOutput <- true
                    command.StandardOutputEncoding <- Text.Encoding.UTF8

                    let result = Process.Start command
                    result.WaitForExit()
                    Seq.contains (result.StandardOutput.ReadLine()) branches
                with ex ->
                    AnsiConsole.MarkupLineInterpolated $"[red]Run git to get branch info failed: {ex.Message}[/]"
                    false

            match ctx.GetMode() with
            | Mode.CommandHelp cmdHelpCtx ->
                if cmdHelpCtx.Verbose then
                    let branchesStr = String.Join(" or ", branches)
                    AnsiConsole.MarkupLineInterpolated $"{ctx.BuildIndent()}when branch is [green]{branchesStr}[/]"
                false
            | Mode.Verification ->
                let branchesStr = String.Join(" or ", branches)
                if getResult () then
                    AnsiConsole.MarkupLineInterpolated $"[green]✓ [/]{ctx.BuildIndent().Substring(2)}when branch is [green]{branchesStr}[/]"
                else
                    AnsiConsole.MarkupLineInterpolated $"[red]✕ [/]{ctx.BuildIndent().Substring(2)}when branch is [red]{branchesStr}[/]"
                false
            | Mode.Execution -> getResult ()

        member ctx.WhenBranch(branch: string) = ctx.WhenBranch [ branch ]


        member ctx.WhenPlatform(platform: OSPlatform) =
            let getResult () = RuntimeInformation.IsOSPlatform platform

            match ctx.GetMode() with
            | Mode.CommandHelp { Verbose = true } ->
                AnsiConsole.MarkupLine $"{ctx.BuildIndent()}when platform is [green]{platform}[/]"
                false
            | Mode.CommandHelp _ -> true
            | Mode.Verification ->
                if getResult () then
                    AnsiConsole.MarkupLine $"[green]✓ [/]{ctx.BuildIndent().Substring(2)}when platform is [green]{platform}[/]"
                else
                    AnsiConsole.MarkupLine $"[red]✕ [/]{ctx.BuildIndent().Substring(2)}when platform is [red]{platform}[/]"
                false
            | Mode.Execution -> getResult ()


    let inline buildConditions ([<InlineIfLambda>] builder: BuildConditions) ([<InlineIfLambda>] conditionsFn) =
        BuildConditions(fun conditions -> builder.Invoke(conditions) @ [ conditionsFn ])


open Internal


type StageContext with

    member ctx.IsOSX = ctx.WhenPlatform(OSPlatform.OSX)
    member ctx.IsLinux = ctx.WhenPlatform(OSPlatform.Linux)
    member ctx.IsWindows = ctx.WhenPlatform(OSPlatform.Windows)

    member ctx.IsBranch(branch: string) = ctx.WhenBranch(branch)


type ConditionsBuilder() =

    member inline _.Yield(_: unit) = BuildConditions(fun x -> x)

    member inline _.Yield(x: BuildStageIsActive) = x

    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildConditions) = BuildConditions(fun conds -> fn().Invoke(conds))
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildStageIsActive) = BuildConditions(fun conds -> conds @ [ fn().Invoke ])


    member inline _.Combine([<InlineIfLambda>] buildStageIsActive: BuildStageIsActive, [<InlineIfLambda>] builder: BuildConditions) =
        BuildConditions(fun conditions -> builder.Invoke(conditions @ [ buildStageIsActive.Invoke ]))


    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildConditions) =
        BuildConditions(fun conds -> fn().Invoke(builder.Invoke(conds)))

    member inline _.For([<InlineIfLambda>] builder: BuildConditions, [<InlineIfLambda>] fn: unit -> BuildStageIsActive) =
        BuildConditions(fun conditions -> builder.Invoke(conditions) @ [ fn().Invoke ])


    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, arg: EnvArg) = buildConditions builder (fun ctx -> ctx.WhenEnvArg(arg))

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string) = buildConditions builder (fun ctx -> ctx.WhenEnvArg(envKey))

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, envValue: string) =
        buildConditions builder (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue))

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, envValue: string, description: string) =
        buildConditions builder (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description))

    [<CustomOperation("envVar")>]
    member inline _.envVar([<InlineIfLambda>] builder: BuildConditions, envKey: string, envValue: string, description: string, isOptional: bool) =
        buildConditions builder (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description, isOptional = isOptional))


    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, arg: CmdArg) = buildConditions builder (fun ctx -> ctx.WhenCmdArg(arg))

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string) =
        buildConditions builder (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName))

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string, argValue: string) =
        buildConditions builder (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue))

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg([<InlineIfLambda>] builder: BuildConditions, argKeyLongName: string, argValue: string, description: string) =
        buildConditions builder (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description))

    [<CustomOperation("cmdArg")>]
    member inline _.cmdArg
        (
            [<InlineIfLambda>] builder: BuildConditions,
            argKeyLongName: string,
            argValue: string,
            description: string,
            isOptional: bool
        ) =
        buildConditions
            builder
            (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description, isOptional = isOptional))


    [<CustomOperation("branch")>]
    member inline _.branch([<InlineIfLambda>] builder: BuildConditions, branch: string) = buildConditions builder (fun ctx -> ctx.WhenBranch(branch))

    /// True if any of the branch is met
    [<CustomOperation("branches")>]
    member inline _.branches([<InlineIfLambda>] builder: BuildConditions, branches: string seq) =
        buildConditions builder (fun ctx -> ctx.WhenBranch(branches))

    [<CustomOperation("platformWindows")>]
    member inline _.platformWindows([<InlineIfLambda>] builder: BuildConditions) =
        buildConditions builder (fun ctx -> ctx.WhenPlatform OSPlatform.Windows)

    [<CustomOperation("platformLinux")>]
    member inline _.platformLinux([<InlineIfLambda>] builder: BuildConditions) =
        buildConditions builder (fun ctx -> ctx.WhenPlatform OSPlatform.Linux)

    [<CustomOperation("platformOSX")>]
    member inline _.platformOSX([<InlineIfLambda>] builder: BuildConditions) = buildConditions builder (fun ctx -> ctx.WhenPlatform OSPlatform.OSX)


type StageBuilder with

    /// Set if stage is active or should run.
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildStage, value: bool) = buildStageIsActive build (fun _ -> value)


    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, arg: EnvArg) = buildStageIsActive build (fun ctx -> ctx.WhenEnvArg(arg))

    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline t_his.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenEnvArg(envKey))

    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, envValue: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue))

    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, envValue: string, description: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description))

    /// Set if stage is active or should run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildStage, envKey: string, envValue: string, description: string, isOptional) =
        buildStageIsActive build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description, isOptional = isOptional))


    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, arg: CmdArg) = buildStageIsActive build (fun ctx -> ctx.WhenCmdArg arg)

    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName))

    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue))

    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string, description: string) =
        buildStageIsActive build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description))

    /// Set if stage is active or should run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildStage, argKeyLongName: string, argValue: string, description: string, isOptional) =
        buildStageIsActive
            build
            (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description, isOptional = isOptional))


    /// Set if stage is active or should run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildStage, branch: string) = buildStageIsActive build (fun ctx -> ctx.WhenBranch branch)

    /// Set if stage is active or should run by check the git branch name.
    [<CustomOperation("whenBranches")>]
    member inline _.whenBranches([<InlineIfLambda>] build: BuildStage, branches: string seq) =
        buildStageIsActive build (fun ctx -> ctx.WhenBranch branches)

    /// Set if stage is active or should run by check the platform is Windows.
    [<CustomOperation("whenWindows")>]
    member inline _.whenWindows([<InlineIfLambda>] build: BuildStage) = buildStageIsActive build (fun ctx -> ctx.WhenPlatform OSPlatform.Windows)

    /// Set if stage is active or should run by check the platform is Linux.
    [<CustomOperation("whenLinux")>]
    member inline _.whenLinux([<InlineIfLambda>] build: BuildStage) = buildStageIsActive build (fun ctx -> ctx.WhenPlatform OSPlatform.Linux)

    /// Set if stage is active or should run by check the platform is OSX.
    [<CustomOperation("whenOSX")>]
    member inline _.whenOSX([<InlineIfLambda>] build: BuildStage) = buildStageIsActive build (fun ctx -> ctx.WhenPlatform OSPlatform.OSX)


type PipelineBuilder with

    /// Set if pipeline can run
    [<CustomOperation("when'")>]
    member inline _.when'([<InlineIfLambda>] build: BuildPipeline, value: bool) = buildPipelineVerification build (fun _ -> value)


    /// Set if pipeline can run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, arg: EnvArg) =
        buildPipelineVerification build (fun ctx -> ctx.WhenEnvArg(arg))

    /// Set if pipeline can run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenEnvArg(envKey))

    /// Set if pipeline can run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, envValue: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue))

    /// Set if pipeline can run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, envValue: string, description: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description))

    /// Set if pipeline can run by check the environment variable.
    [<CustomOperation("whenEnvVar")>]
    member inline _.whenEnvVar([<InlineIfLambda>] build: BuildPipeline, envKey: string, envValue: string, description: string, isOptional) =
        buildPipelineVerification build (fun ctx -> ctx.WhenEnvArg(envKey, argValue = envValue, description = description, isOptional = isOptional))


    /// Set if pipeline can run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, arg: CmdArg) =
        buildPipelineVerification build (fun ctx -> ctx.WhenCmdArg(arg))

    /// Set if pipeline can run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName))

    /// Set if pipeline can run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string, argValue: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue))

    /// Set if pipeline can run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg([<InlineIfLambda>] build: BuildPipeline, argKeyLongName: string, argValue: string, description: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description))

    /// Set if pipeline can run by check the command line args.
    [<CustomOperation("whenCmdArg")>]
    member inline _.whenCmdArg
        (
            [<InlineIfLambda>] build: BuildPipeline,
            argKeyLongName: string,
            argValue: string,
            description: string,
            isOptional: bool
        ) =
        buildPipelineVerification
            build
            (fun ctx ->
                ctx

                    .WhenCmdArg(CmdName.LongName argKeyLongName, argValue = argValue, description = description, isOptional = isOptional)
            )


    /// Set if pipeline can run by check the git branch name.
    [<CustomOperation("whenBranch")>]
    member inline _.whenBranch([<InlineIfLambda>] build: BuildPipeline, branch: string) =
        buildPipelineVerification build (fun ctx -> ctx.WhenBranch branch)

    /// Set if pipeline can run by check the git branch name.
    [<CustomOperation("whenBranches")>]
    member inline _.whenBranches([<InlineIfLambda>] build: BuildPipeline, branches: string seq) =
        buildPipelineVerification build (fun ctx -> ctx.WhenBranch branches)

    /// Set if pipeline can run by check the platform is Windows.
    [<CustomOperation("whenWindows")>]
    member inline _.whenWindows([<InlineIfLambda>] build: BuildPipeline) =
        buildPipelineVerification build (fun ctx -> ctx.WhenPlatform OSPlatform.Windows)

    /// Set if pipeline can run by check the platform is Linux.
    [<CustomOperation("whenLinux")>]
    member inline _.whenLinux([<InlineIfLambda>] build: BuildPipeline) =
        buildPipelineVerification build (fun ctx -> ctx.WhenPlatform OSPlatform.Linux)

    /// Set if pipeline can run by check the platform is OSX.
    [<CustomOperation("whenOSX")>]
    member inline _.whenOSX([<InlineIfLambda>] build: BuildPipeline) = buildPipelineVerification build (fun ctx -> ctx.WhenPlatform OSPlatform.OSX)


type WhenAnyBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.GetMode() with
            | Mode.Execution -> builder.Invoke [] |> Seq.exists (fun fn -> fn ctx)
            | _ ->
                match ctx.GetMode() with
                | Mode.Verification
                | Mode.CommandHelp { Verbose = true } -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when any below conditions are met[/]"
                | _ -> ()

                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false
        )


type WhenAllBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.GetMode() with
            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> fn ctx) |> Seq.reduce (fun x y -> x && y)
            | _ ->
                match ctx.GetMode() with
                | Mode.Verification
                | Mode.CommandHelp { Verbose = true } -> AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are met[/]"
                | _ -> ()

                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false
        )


type WhenNotBuilder() =
    inherit ConditionsBuilder()

    member inline _.Run([<InlineIfLambda>] builder: BuildConditions) =
        BuildStageIsActive(fun ctx ->
            match ctx.GetMode() with
            | Mode.Execution -> builder.Invoke [] |> Seq.map (fun fn -> not (fn ctx)) |> Seq.reduce (fun x y -> x && y)
            | mode ->
                match mode with
                | Mode.Verification
                | Mode.CommandHelp { Verbose = true } ->
                    AnsiConsole.MarkupLine $"[olive]{ctx.BuildIndent()}when all below conditions are [bold red]NOT[/] met[/]"
                | _ -> ()

                let indentCtx =
                    { StageContext.Create "  " with
                        ParentContext = ctx.ParentContext
                    }
                let newCtx =
                    { ctx with
                        ParentContext = ValueSome(StageParent.Stage indentCtx)
                    }
                builder.Invoke [] |> Seq.iter (fun fn -> fn newCtx |> ignore)
                false
        )


type WhenCmdBuilder() =

    member _.Run(build: BuildCmdInfo) =
        BuildStageIsActive(fun ctx ->
            let cmdInfo =
                build.Invoke
                    {
                        // We should carefully procees the empty string in this build type
                        Name = CmdName.ShortName ""
                        Description = None
                        Values = []
                        IsOptional = false
                    }
            ctx.WhenCmdArg(cmdInfo)
        )

    member inline _.Yield(_: unit) = BuildCmdInfo(fun x -> x)
    member inline _.Yield(x: BuildCmdInfo) = x
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildCmdInfo) = BuildCmdInfo(fun x -> fn().Invoke(x))


    /// Short name, long name
    [<CustomOperation "fullName">]
    member inline _.fullName([<InlineIfLambda>] build: BuildCmdInfo, shortName: string, longName: string) =
        BuildCmdInfo(fun info ->
            { build.Invoke(info) with
                Name = CmdName.FullName(shortName, longName)
            }
        )

    /// It is the same as shortName
    [<CustomOperation "name">]
    member inline this.name([<InlineIfLambda>] build: BuildCmdInfo, x: string) = this.shortName (build, x)

    /// It is the same as longName
    [<CustomOperation "alias">]
    member inline this.alias([<InlineIfLambda>] build: BuildCmdInfo, x: string) = this.longName (build, x)


    [<CustomOperation "shortName">]
    member _.shortName(build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info ->
            let info = build.Invoke(info)
            { info with
                Name =
                    match info.Name with
                    | CmdName.FullName(_, longName)
                    | CmdName.LongName longName when not (String.IsNullOrEmpty longName) -> CmdName.FullName(x, longName)
                    | _ -> CmdName.ShortName x
            }
        )

    [<CustomOperation "longName">]
    member _.longName(build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info ->
            let info = build.Invoke(info)
            { info with
                Name =
                    match info.Name with
                    | CmdName.FullName(shortName, _)
                    | CmdName.ShortName shortName when not (String.IsNullOrEmpty shortName) -> CmdName.FullName(shortName, x)
                    | _ -> CmdName.LongName x
            }
        )


    [<CustomOperation "description">]
    member inline _.description([<InlineIfLambda>] build: BuildCmdInfo, x: string) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Description = Some x })

    [<CustomOperation "value">]
    member inline _.value([<InlineIfLambda>] build: BuildCmdInfo, x: string) = BuildCmdInfo(fun info -> { build.Invoke(info) with Values = [ x ] })

    [<CustomOperation "acceptValues">]
    member inline _.acceptValues([<InlineIfLambda>] build: BuildCmdInfo, values: string list) =
        BuildCmdInfo(fun info -> { build.Invoke(info) with Values = values })

    [<CustomOperation "optional">]
    member inline _.optional([<InlineIfLambda>] build: BuildCmdInfo) = BuildCmdInfo(fun info -> { build.Invoke(info) with IsOptional = true })


type WhenEnvBuilder() =

    member _.Run(build: BuildEnvInfo) =
        BuildStageIsActive(fun ctx ->
            let arg =
                build.Invoke
                    {
                        // We should carefully procees the empty string in this build type
                        Name = ""
                        Description = None
                        Values = []
                        IsOptional = false
                    }
            ctx.WhenEnvArg(arg)
        )

    member inline _.Yield(_: unit) = BuildEnvInfo(fun x -> x)
    member inline _.Yield(x: BuildEnvInfo) = x
    member inline _.Delay([<InlineIfLambda>] fn: unit -> BuildEnvInfo) = BuildEnvInfo(fun x -> fn().Invoke(x))


    /// ENV variable
    [<CustomOperation "name">]
    member inline _.name([<InlineIfLambda>] build: BuildEnvInfo, name: string) = BuildEnvInfo(fun info -> { build.Invoke(info) with Name = name })

    [<CustomOperation "description">]
    member inline _.description([<InlineIfLambda>] build: BuildEnvInfo, x: string) =
        BuildEnvInfo(fun info -> { build.Invoke(info) with Description = Some x })

    [<CustomOperation "value">]
    member inline _.value([<InlineIfLambda>] build: BuildEnvInfo, x: string) = BuildEnvInfo(fun info -> { build.Invoke(info) with Values = [ x ] })

    [<CustomOperation "acceptValues">]
    member inline _.acceptValues([<InlineIfLambda>] build: BuildEnvInfo, values: string list) =
        BuildEnvInfo(fun info -> { build.Invoke(info) with Values = values })

    [<CustomOperation "optional">]
    member inline _.optional([<InlineIfLambda>] build: BuildEnvInfo) = BuildEnvInfo(fun info -> { build.Invoke(info) with IsOptional = true })


/// When any of the added conditions are satisified, the stage will be active
let whenAny = WhenAnyBuilder()
/// When all of the added conditions are satisified, the stage will be active
let whenAll = WhenAllBuilder()
/// When all of the added conditions are not satisified, the stage will be active
let whenNot = WhenNotBuilder()
/// When the cmd is matched, the stage will be active
let whenCmd = WhenCmdBuilder()
/// When the ENV is matched, the stage will be active
let whenEnv = WhenEnvBuilder()
