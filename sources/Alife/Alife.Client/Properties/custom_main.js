const { app } = require('electron');

module.exports.onStartup = function (host) {

    app.commandLine.appendSwitch('no-sandbox');
    app.commandLine.appendSwitch('disable-gpu');
    app.disableHardwareAcceleration();

    app.on('gpu-info-update', () => {
        console.log(
            'no-sandbox:',
            app.commandLine.hasSwitch('no-sandbox')
        );
        console.log(
            'disable-gpu:',
            app.commandLine.hasSwitch('disable-gpu')
        );
        console.log(
            'GPU acceleration:',
            app.getGPUFeatureStatus()
        );
    });

    return true;
};