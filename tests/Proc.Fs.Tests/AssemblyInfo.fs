module Proc.Fs.Tests.AssemblyInfo

open TUnit.Core

// xUnit ran these one at a time (parallelizeTestCollections: false). Several tests swap Console.Out or set environment
// variables of this process, and many time child processes: keep running them serially
[<assembly: NotInParallel>]
do ()
