using Alife.Test.Framework;
using NUnitLite;

new AutoRun().Execute(args.Length > 0
    ? args
    : [
        "--test",
        typeof(FrameworkTests).FullName
    ]);