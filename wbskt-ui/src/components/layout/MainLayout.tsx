import { Outlet } from 'react-router-dom';
import { Header } from './Header';
import { Sidebar } from './Sidebar';

export const MainLayout = () => {
  return (
    <div className="h-screen flex flex-col">
      <Sidebar />
      <div className="pl-64 flex-grow flex flex-col">
        <Header />
        <main className="flex-grow p-6 bg-gray-50">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
