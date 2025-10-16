import { NavLink } from 'react-router-dom';

const navigation = [
  { name: 'Dashboard', href: '/dashboard' },
  { name: 'Workflows', href: '/workflows' },
  { name: 'Executions', href: '/executions' },
  { name: 'Clients', href: '/clients' },
  { name: 'Policies', href: '/policies' },
  { name: 'Integrations', href: '/integrations' },
];

export const Sidebar = () => {
  return (
    <nav className="fixed top-0 left-0 h-full w-64 bg-gray-800 text-white flex flex-col">
      <div className="h-16 flex items-center justify-center text-2xl font-bold">
        WBSKT
      </div>
      <ul className="flex-grow px-2">
        {navigation.map((item) => (
          <li key={item.name}>
            <NavLink
              to={item.href}
              className={({ isActive }) =>
                `block px-4 py-2 my-1 rounded-md text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-gray-900 text-white'
                    : 'text-gray-300 hover:bg-gray-700 hover:text-white'
                }`
              }
            >
              {item.name}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
};
