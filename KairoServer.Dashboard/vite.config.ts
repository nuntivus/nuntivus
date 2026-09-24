import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import { readFileSync } from 'node:fs';

const packageJson = JSON.parse(
    readFileSync(new URL('./package.json', import.meta.url), 'utf-8')
) as { version?: string }

const appVersion = packageJson.version?.trim() || '0.0.0'

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    define: {
        __APP_VERSION__: JSON.stringify(appVersion),
    },
    server: {
        port: 54097,
    }
})
