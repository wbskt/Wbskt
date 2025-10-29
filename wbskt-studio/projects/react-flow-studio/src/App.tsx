import ReactFlow, { Controls, Background, MiniMap, ReactFlowProvider } from 'reactflow';
import 'reactflow/dist/style.css';
import './index.scss';

const initialNodes = [
  { id: '1', position: { x: 0, y: 0 }, data: { label: 'Hello' } },
  { id: '2', position: { x: 100, y: 100 }, data: { label: 'World' } },
];
const initialEdges = [{ id: 'e1-2', source: '1', target: '2' }];

function App() {
  return (
    <div style={{ width: '100vw', height: '100vh' }}>
      <ReactFlowProvider>
        <ReactFlow nodes={initialNodes} edges={initialEdges} fitView>
          <Controls />
          <MiniMap />
          <Background variant="dots" gap={12} size={1} />
        </ReactFlow>
      </ReactFlowProvider>
    </div>
  );
}

export default App;