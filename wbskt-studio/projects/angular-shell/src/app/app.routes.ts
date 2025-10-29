import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout/layout';
import { authGuard } from './core/auth/auth-guard';

export const routes: Routes = [
    {
        path: 'login',
        loadChildren: () => import('./modules/auth/auth-module').then(m => m.AuthModule)
    },
    {
        path: '',
        component: LayoutComponent,
        canActivate: [authGuard],
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
            {
                path: 'studio/:workflowId',
                loadChildren: () => import('./modules/studio/studio-module').then(m => m.StudioModule)
            },
        ]
    }
];