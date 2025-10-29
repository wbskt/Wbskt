import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StudioPage } from './studio-page';

describe('StudioPage', () => {
  let component: StudioPage;
  let fixture: ComponentFixture<StudioPage>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudioPage]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StudioPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
