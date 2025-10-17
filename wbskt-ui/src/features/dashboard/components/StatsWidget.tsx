import { Card } from "../../../components/common/Card";

interface StatsWidgetProps {
  title: string;
  value: number | string;
  icon?: React.ReactNode;
}

export const StatsWidget = ({ title, value, icon }: StatsWidgetProps) => {
  return (
    <Card className="p-4">
      <div className="flex items-center">
        {icon && <div className="mr-4">{icon}</div>}
        <div>
          <p className="text-sm font-medium text-gray-500 truncate">{title}</p>
          <p className="mt-1 text-3xl font-semibold text-gray-900">{value}</p>
        </div>
      </div>
    </Card>
  );
};
