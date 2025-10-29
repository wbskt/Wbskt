# WBSKT Studio Project Overview

This project is an Angular workspace that serves as the frontend for the WBSKT application. It currently comprises two main sub-projects:

1.  **`angular-shell`**: The main Angular application that provides the overall user interface and structure.
2.  **`react-flow-studio`**: A React application, built with Vite, that is exposed as a web component (`<wbskt-react-flow>`) and integrated into the Angular shell. This component provides a visual workflow editor using React Flow.

## Technologies Used

*   **Angular**: Frontend framework for the main shell application.
*   **React**: Frontend library for the workflow studio web component.
*   **Vite**: Fast build tool for the React Flow Studio.
*   **React Flow**: Library for building node-based editors and interactive diagrams within the `react-flow-studio`.
*   **Zustand**: State management library used in `react-flow-studio`.
*   **React Dnd**: Drag-and-drop library used in `react-flow-studio` for node creation.
*   **TypeScript**: Primary language for both Angular and React projects.
*   **Sass**: CSS preprocessor used for styling.

## Project Structure

The project is organized as an Angular monorepo:

*   `/`: Root of the Angular workspace, containing `angular.json`, `package.json`, etc.
*   `projects/angular-shell/`: Contains the Angular application.
*   `projects/react-flow-studio/`: Contains the React application that builds into a web component.

## Building and Running

### Angular Shell Application

To run the Angular shell application:

```bash
npm install # Install root dependencies
ng serve projects/angular-shell # Starts the development server for the Angular app
```

The application will be accessible at `http://localhost:4200/`.

To build the Angular shell application:

```bash
ng build projects/angular-shell
```

The build artifacts will be stored in the `dist/projects/angular-shell` directory.

To run unit tests for the Angular shell:

```bash
ng test projects/angular-shell
```

### React Flow Studio Web Component

The `react-flow-studio` project is built as a web component and integrated into the Angular shell.

To develop the `react-flow-studio` independently:

```bash
cd projects/react-flow-studio
npm install # Install dependencies for the React project
npm run dev # Starts the Vite development server
```

To build the `react-flow-studio` web component:

```bash
cd projects/react-flow-studio
npm run build
```

This will generate the web component bundle in `projects/react-flow-studio/dist`. The Angular application will then consume this bundle.

## Integration of React Flow Studio

The `react-flow-studio` is exposed as a custom web component `<wbskt-react-flow>`. It communicates with its parent (the Angular shell) via custom DOM events.

*   **Input**: The web component accepts a `workflow` prop (a JSON string) to load existing workflow data.
*   **Output**: When the workflow data (nodes or edges) changes within the React Flow Studio, it dispatches a `workflowUpdated` custom event. The `detail` of this event contains `workflowId` and `workflowData` (serialized nodes and edges).

## Development Conventions

*   **TypeScript**: Explicit types are used for props, variables, and function signatures.
*   **State Management**: `zustand` is used for state management within the `react-flow-studio` web component.
*   **Component-based Architecture**: Both Angular and React projects follow a component-based architecture.
*   **API Security**: `RefId` (GUID) is used instead of integer IDs for exposed resources.
*   **Event-Driven Communication**: Custom events are used for communication between the React web component and the Angular host application.
