import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  css: {
    preprocessorOptions: {
      scss: {
        // You can add global SCSS variables or mixins here if needed
        // For now, we'll rely on importing from the Angular project's shared styles
      },
    },
  },
  build: {
    lib: {
      entry: 'src/web-component.tsx',
      name: 'WbsktReactFlow',
      fileName: 'wbskt-react-flow',
      formats: ['es', 'umd'],
    },
    rollupOptions: {
      external: ['react', 'react-dom', 'react-dom/client'],
      output: {
        globals: {
          react: 'React',
          'react-dom': 'ReactDOM',
          'react-dom/client': 'ReactDOMClient',
        },
      },
    },
  },
});
