import { Dropdown, DropdownItem } from '../common/Dropdown';

export const Header = () => {
  // Placeholder user data
  const user = { name: 'Richard Joy', email: 'richard@example.com' };

  return (
    <header className="bg-white shadow-sm border-b border-gray-200">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center h-16">
          <h1 className="text-xl font-semibold text-gray-900">Dashboard</h1>
          <div className="ml-4 flex items-center md:ml-6">
            <Dropdown
              button={
                <div className="flex items-center space-x-2 cursor-pointer">
                  <span className="text-sm font-medium text-gray-700">{user.name}</span>
                  <div className="h-8 w-8 rounded-full bg-gray-300 flex items-center justify-center text-white font-bold">
                    {user.name.charAt(0)}
                  </div>
                </div>
              }
            >
              <DropdownItem>Your Profile</DropdownItem>
              <DropdownItem>Settings</DropdownItem>
              <DropdownItem>Logout</DropdownItem>
            </Dropdown>
          </div>
        </div>
      </div>
    </header>
  );
};
