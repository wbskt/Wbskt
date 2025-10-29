import React from 'react';
import { createRoot } from 'react-dom/client';
import reactToWebComponent from 'react-to-webcomponent';
import App from './App';

// Define the custom element
const WbsktReactFlowElement = reactToWebComponent(App, React, createRoot, {
  props: {
    workflow: 'string', // Define props that the web component will accept
  },
  shadow: false, // Set to true if you want Shadow DOM
});

// Register the custom element
customElements.define('wbskt-react-flow', WbsktReactFlowElement);
