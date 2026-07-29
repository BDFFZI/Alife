using Alife.Test.Framework;
using NUnitLite;

new AutoRun().Execute([
    "--test",
    typeof(FrameworkTests).FullName
]);
