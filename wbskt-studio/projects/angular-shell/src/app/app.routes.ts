import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout/layout';

export const routes: Routes = [
    {
        path: '',
        component: LayoutComponent,
        children: [
            { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
            {
                path: 'dashboard',
                loadChildren: () => import('./modules/dashboard/dashboard-module').then(m => m.DashboardModule)
            },
            {
                path: 'workflows',
                loadChildren: () => import('./modules/workflows/workflows-module').then(m => m.WorkflowsModule)
            },
        ]
    }
];