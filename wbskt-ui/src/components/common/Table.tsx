import type { ReactNode } from 'react';
import { Spinner } from './Spinner';

// Generic type for a column definition
export interface ColumnDef<T> {
  header: string;
  accessorKey: keyof T;
  cell?: (row: T) => ReactNode;
}

interface TableProps<T> {
  data: T[];
  columns: ColumnDef<T>[];
  isLoading?: boolean;
  emptyState?: ReactNode;
}

export const Table = <T extends { id: string | number }>({ data, columns, isLoading, emptyState }: TableProps<T>) => {
  if (isLoading) {
    return (
      <div className="flex justify-center items-center p-8">
        <Spinner size="lg" />
      </div>
    );
  }

  if (!data || data.length === 0) {
    return <div className="text-center py-8 text-gray-500">{emptyState ?? 'No data available.'}</div>;
  }

  return (
    <div className="overflow-x-auto shadow border-b border-gray-200 sm:rounded-lg">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            {columns.map((column) => (
              <th
                key={column.accessorKey as string}
                scope="col"
                className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="bg-white divide-y divide-gray-200">
          {data.map((row) => (
            <tr key={row.id} className="hover:bg-gray-50">
              {columns.map((column) => (
                <td key={`${row.id}-${column.accessorKey as string}`} className="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                  {column.cell ? column.cell(row) : (row[column.accessorKey] as ReactNode)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};
