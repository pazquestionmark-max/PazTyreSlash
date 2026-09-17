fx_version 'cerulean'
game 'gta5'

name 'paz_tyre_slashing'
description 'Slash individual vehicle tyres with a blade (QBox, ox_target, ox_inventory)'
author 'Pazzer'
version '1.1.0'
repository 'https://github.com/<your-account>/paz_tyre_slashing'
license 'MIT'

dependencies {
    '/onesync',
    'ox_lib',
    'qbx_core',
    'ox_inventory',
    'ox_target',
}

-- Build with `dotnet build -c Release` in server/ (see README).
server_script 'server/bin/PazTyreSlashing.Server.net.dll'

client_scripts {
    'client/utils.js',
    'client/tyre.js',
    'client/animation.js',
    'client/minigame.js',
    'client/main.js',
    'client/targeting.js',
}

ui_page 'nui/index.html'

files {
    'config/config.json',
    'nui/index.html',
    'nui/style.css',
    'nui/app.js',
}
