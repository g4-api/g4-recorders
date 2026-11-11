using G4.Recorders.Android.Domain.Adb;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Android.Domain.UnitTests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.UnitTests
{
    [TestClass]
    [TestCategory(nameof(AndroidRecorderLauncher))]
    [TestCategory("UnitTest")]
    public sealed class AndroidRecorderLauncherTests
    {
        // Where the launcher looks for the recorder APK: the test output folder, like a published host.
        private static readonly string ApkPath = Path.Combine(AppContext.BaseDirectory, "G4.Recorders.Android.App.Drop", "g4-recorder.apk");

        // The enabled-services value the launcher writes on a device with no other service.
        private const string RecorderServiceArgument = "'" + AndroidRecorderLauncher.RecorderServiceComponent + "'";

        [TestInitialize]
        public void CreateRecorderApk()
        {
            // Arrange: put a stand-in APK where the host project copies the real one; adb is fake, so its content is unused.
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            File.WriteAllText(ApkPath, "apk");
        }

        [TestMethod(DisplayName = "Verify that the shell argument is single-quoted with embedded quotes escaped")]
        public void ConvertToShellArgumentTest()
        {
            // Act and Assert: plain, empty and quoted values all arrive at the device shell as one argument.
            Assert.AreEqual("'plain'", AndroidRecorderLauncher.ConvertToShellArgument("plain"));
            Assert.AreEqual("''", AndroidRecorderLauncher.ConvertToShellArgument(""));
            Assert.AreEqual("'it'\\''s'", AndroidRecorderLauncher.ConvertToShellArgument("it's"));
        }

        [TestMethod(DisplayName = "Verify that the screenshot reads the override size and Base64-encodes the PNG")]
        public async Task GetScreenshotTestAsync()
        {
            // Arrange: a device with an override size and a fake PNG.
            var adb = new FakeAdbRunner()
                .AddHealthyDevice("emulator-5554")
                .Add("wm size", "Physical size: 1080x2400\nOverride size: 720x1600")
                .Add("screencap", "PNG");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: read the metrics only, then the full capture.
            var metrics = await launcher.GetScreenshotAsync(metricsOnly: true);
            var screenshot = await launcher.GetScreenshotAsync(metricsOnly: false);

            // Assert: the override size wins, the metrics carry no image, and the capture is the PNG in Base64.
            Assert.AreEqual(720, metrics.Width);
            Assert.AreEqual(1600, metrics.Height);
            Assert.IsNull(metrics.ImageBase64);
            Assert.AreEqual(Convert.ToBase64String("PNG"u8.ToArray()), screenshot.ImageBase64);
            Assert.AreEqual("image/png", screenshot.MimeType);
        }

        [TestMethod(DisplayName = "Verify that the recorder service is added once to the enabled services")]
        public void NewEnabledServicesTest()
        {
            // Arrange: the component the launcher appends.
            var component = AndroidRecorderLauncher.RecorderServiceComponent;

            // Act and Assert: unset values become the recorder alone, others are kept, and a present one is not repeated.
            Assert.AreEqual(component, AndroidRecorderLauncher.NewEnabledServices(null));
            Assert.AreEqual(component, AndroidRecorderLauncher.NewEnabledServices("null"));
            Assert.AreEqual($"a/.A:{component}", AndroidRecorderLauncher.NewEnabledServices("a/.A"));
            Assert.AreEqual($"{component}:a/.A", AndroidRecorderLauncher.NewEnabledServices($"{component}:a/.A"));
        }

        [TestMethod(DisplayName = "Verify that the user's own accessibility services stay enabled")]
        public async Task StartKeepsOtherServicesTestAsync()
        {
            // Arrange: a screen reader is already on.
            var adb = new FakeAdbRunner()
                .AddHealthyDevice("emulator-5554")
                .Add("settings get secure enabled_accessibility_services", "com.reader/.ReaderService");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording.
            await launcher.StartAsync(NewParameters());

            // Assert: the recorder is appended to the existing list.
            var expected = $"-s emulator-5554 shell settings put secure enabled_accessibility_services 'com.reader/.ReaderService:{AndroidRecorderLauncher.RecorderServiceComponent}'";
            Assert.Contains(expected, adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that a missing recorder APK fails before any device command")]
        public async Task StartMissingApkTestAsync()
        {
            // Arrange: a host published without the recorder app.
            File.Delete(ApkPath);
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording.
            await Assert.ThrowsAsync<FileNotFoundException>(() => launcher.StartAsync(NewParameters()));

            // Assert: the device was never touched.
            Assert.IsEmpty(adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that a missing adb surfaces as a file-not-found failure")]
        public async Task StartNoAdbTestAsync()
        {
            // Arrange: adb cannot be found on the host.
            var adb = new FakeAdbRunner().AddFailure("devices", new FileNotFoundException("adb was not found on the recorder host."));
            var (launcher, _, _) = NewLauncher(adb);

            // Act and Assert: the failure keeps its type, which the hub and controller map to a caller-visible error.
            await Assert.ThrowsAsync<FileNotFoundException>(() => launcher.StartAsync(NewParameters()));
        }

        [TestMethod(DisplayName = "Verify that no connected device fails with a hint to check adb devices")]
        public async Task StartNoDeviceTestAsync()
        {
            // Arrange: adb lists no device.
            var adb = new FakeAdbRunner().AddHealthyDevice("x").Add("devices", "List of devices attached\n");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.StartAsync(NewParameters()));

            // Assert: the message tells the operator what to check.
            Assert.Contains("adb devices", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that a recording without an application returns a negative id and reads no pid")]
        public async Task StartNothingToStartTestAsync()
        {
            // Arrange: a healthy device and capabilities that name no application.
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start two recordings.
            var firstId = await launcher.StartAsync(NewParameters());
            var secondId = await launcher.StartAsync(NewParameters());

            // Assert: each gets its own negative id and no pid is read.
            Assert.IsLessThan(0, firstId);
            Assert.AreNotEqual(firstId, secondId);
            Assert.DoesNotContain(command => command.Contains("pidof", StringComparison.Ordinal), adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that a package without an activity starts its launcher activity through monkey")]
        public async Task StartPackageOnlyTestAsync()
        {
            // Arrange: a healthy device.
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording that names only the package.
            await launcher.StartAsync(NewParameters(udid: "", package: "com.example.app", activity: ""));

            // Assert: monkey starts the launcher activity of the package.
            Assert.Contains("-s emulator-5554 shell monkey -p 'com.example.app' -c android.intent.category.LAUNCHER 1", adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that start runs the device sequence in order and returns the application pid")]
        public async Task StartSequenceTestAsync()
        {
            // Arrange: one healthy emulator.
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording of a named activity.
            var processId = await launcher.StartAsync(NewParameters(udid: "", package: "com.example.app", activity: ".LoginActivity"));

            // Assert: the pid of the started application is the session id.
            Assert.AreEqual(4242, processId);

            // Assert: the recorder app gets its values before its service is enabled, and the application starts last.
            string[] expected =
            [
                "devices",
                $"-s emulator-5554 install -r -g {ApkPath}",
                "-s emulator-5554 reverse tcp:9958 tcp:9958",
                "-s emulator-5554 shell am start -n com.g4.recorders.android/.MainActivity --es hubUrl 'http://localhost:9958/hub/v4/g4/peek' --es targetPackage 'com.example.app' --es deviceSerial 'emulator-5554'",
                "-s emulator-5554 shell settings get secure enabled_accessibility_services",
                "-s emulator-5554 shell settings get secure accessibility_enabled",
                $"-s emulator-5554 shell settings put secure enabled_accessibility_services {RecorderServiceArgument}",
                "-s emulator-5554 shell settings put secure accessibility_enabled 1",
                "-s emulator-5554 shell am start -n 'com.example.app/.LoginActivity'",
                "-s emulator-5554 shell pidof 'com.example.app'"
            ];
            Assert.AreSequenceEqual(expected, adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that an APK signed by another machine is reinstalled")]
        public async Task StartSignatureMismatchTestAsync()
        {
            // Arrange: the first install is refused as an incompatible update, the second succeeds.
            var installs = 0;
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var scripted = new ScriptedInstallRunner(adb, () => ++installs == 1 ? "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]" : "Success");
            var launcher = new AndroidRecorderLauncher(new AndroidRecorderRepository(new FakeHubContext()), scripted, new FakeServer("http://+:9958"), NullLogger<AndroidRecorderLauncher>.Instance);

            // Act: start a recording.
            await launcher.StartAsync(NewParameters());

            // Assert: uninstall sits between the two installs.
            var recorderCommands = adb.Commands.Where(i => i.Contains("install", StringComparison.Ordinal)).ToArray();
            string[] expected = [$"-s emulator-5554 install -r -g {ApkPath}", "-s emulator-5554 uninstall com.g4.recorders.android", $"-s emulator-5554 install -r -g {ApkPath}"];
            Assert.AreSequenceEqual(expected, recorderCommands);
        }

        [TestMethod(DisplayName = "Verify that two devices without appium:udid fail and name both serials")]
        public async Task StartTwoDevicesTestAsync()
        {
            // Arrange: two usable devices and one unauthorized one.
            var adb = new FakeAdbRunner().AddHealthyDevice("x").Add("devices", "List of devices attached\nemulator-5554\tdevice\nR58M\tdevice\nZY22\tunauthorized\n");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording without a serial.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.StartAsync(NewParameters()));

            // Assert: the usable serials are named and the unauthorized one is not.
            Assert.Contains("emulator-5554, R58M", exception.Message);
            Assert.DoesNotContain("ZY22", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that appium:udid selects one of several devices")]
        public async Task StartUdidTestAsync()
        {
            // Arrange: two usable devices.
            var adb = new FakeAdbRunner().AddHealthyDevice("x").Add("devices", "List of devices attached\nemulator-5554\tdevice\nR58M\tdevice\n");
            var (launcher, _, _) = NewLauncher(adb);

            // Act: start a recording on the second one.
            await launcher.StartAsync(NewParameters(udid: "R58M", package: "", activity: ""));

            // Assert: every device command targets the requested serial.
            Assert.Contains("-s R58M reverse tcp:9958 tcp:9958", adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that a failed restore step does not skip the remaining steps")]
        public async Task StopContinuesAfterFailureTestAsync()
        {
            // Arrange: a started recording whose settings delete fails.
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, _, _) = NewLauncher(adb);
            var processId = await launcher.StartAsync(NewParameters());
            adb.Add("settings delete", "", exitCode: 1);

            // Act: stop it.
            var isStopped = await launcher.StopAsync(processId);

            // Assert: stop still reports success and the port mapping is still removed.
            Assert.IsTrue(isStopped);
            Assert.Contains("-s emulator-5554 reverse --remove tcp:9958", adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that stop closes the app, stops the application and restores the device")]
        public async Task StopRestoresDeviceTestAsync()
        {
            // Arrange: a registered app that disconnects when asked to close, and a started recording.
            var adb = new FakeAdbRunner().AddHealthyDevice("emulator-5554");
            var (launcher, hub, repository) = NewLauncher(adb);
            repository.Register("app-1");
            hub.OnSend = (_, method, _) =>
            {
                if (method == "CloseRecorder")
                {
                    repository.Remove("app-1");
                }
            };
            var processId = await launcher.StartAsync(NewParameters(udid: "", package: "com.example.app", activity: ""));
            adb.Commands.Clear();

            // Act: stop it.
            var isStopped = await launcher.StopAsync(processId);

            // Assert: the app was asked to close, then every device change was undone.
            Assert.IsTrue(isStopped);
            Assert.AreEqual("CloseRecorder", hub.Sent.Single().Method);
            string[] expected =
            [
                "-s emulator-5554 shell am force-stop com.example.app",
                "-s emulator-5554 shell settings delete secure enabled_accessibility_services",
                "-s emulator-5554 shell settings delete secure accessibility_enabled",
                "-s emulator-5554 reverse --remove tcp:9958"
            ];
            Assert.AreSequenceEqual(expected, adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that stop writes back the settings the device had before recording")]
        public async Task StopRestoresPreviousValuesTestAsync()
        {
            // Arrange: a device with a screen reader on, and a started recording with no application.
            var adb = new FakeAdbRunner()
                .AddHealthyDevice("emulator-5554")
                .Add("settings get secure enabled_accessibility_services", "com.reader/.ReaderService")
                .Add("settings get secure accessibility_enabled", "1");
            var (launcher, _, _) = NewLauncher(adb);
            var processId = await launcher.StartAsync(NewParameters());

            // Act: stop it.
            await launcher.StopAsync(processId);

            // Assert: both settings are written back, and nothing is force-stopped because nothing was started.
            Assert.Contains("-s emulator-5554 shell settings put secure enabled_accessibility_services 'com.reader/.ReaderService'", adb.Commands);
            Assert.Contains("-s emulator-5554 shell settings put secure accessibility_enabled '1'", adb.Commands);
            Assert.DoesNotContain(command => command.Contains("force-stop", StringComparison.Ordinal), adb.Commands);
        }

        [TestMethod(DisplayName = "Verify that an unknown id is not stopped")]
        public async Task StopUnknownIdTestAsync()
        {
            // Arrange: a launcher that started nothing.
            var adb = new FakeAdbRunner();
            var (launcher, _, _) = NewLauncher(adb);

            // Act and Assert: the stop is refused without touching any device.
            Assert.IsFalse(await launcher.StopAsync(1234));
            Assert.IsEmpty(adb.Commands);
        }

        // Builds a launcher over a fake adb, a fresh repository and a host listening on 9958; the caller owns the fakes.
        private static (AndroidRecorderLauncher Launcher, FakeHubContext Hub, AndroidRecorderRepository Repository) NewLauncher(FakeAdbRunner adb)
        {
            // Arrange: share one hub context between the repository and the test, so sends are observable.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            var launcher = new AndroidRecorderLauncher(repository, adb, new FakeServer("http://+:9958"), NullLogger<AndroidRecorderLauncher>.Instance);

            return (launcher, hub, repository);
        }

        // Builds driver parameters with no Appium capabilities set.
        private static DriverParametersModel NewParameters()
        {
            return NewParameters(udid: "", package: "", activity: "");
        }

        // Builds driver parameters with the Appium capabilities the launcher reads.
        private static DriverParametersModel NewParameters(string udid, string package, string activity)
        {
            var alwaysMatch = new AlwaysMatchModel { AppActivity = activity, AppPackage = package, Udid = udid };

            return new DriverParametersModel { Capabilities = new CapabilitiesModel { AlwaysMatch = alwaysMatch } };
        }

        // Wraps the fake runner and answers every install from a script, so a test can vary consecutive installs; every
        // other command goes to the fake, which records it.
        private sealed class ScriptedInstallRunner(FakeAdbRunner inner, Func<string> installOutput) : IAdbRunner
        {
            public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout)
            {
                // Answer installs from the script after recording them like the fake does.
                var command = string.Join(' ', arguments);

                if (command.Contains(" install ", StringComparison.Ordinal))
                {
                    inner.Commands.Add(command);
                    var output = Encoding.UTF8.GetBytes(installOutput());

                    return Task.FromResult(new AdbResult(0, output, string.Empty));
                }

                // Pass every other command to the fake.
                return inner.RunAsync(arguments, timeout);
            }
        }
    }
}
